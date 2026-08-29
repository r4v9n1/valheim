$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceRoot = Split-Path -Parent $ProjectRoot
$ValheimSourceRoot = Split-Path -Parent $SourceRoot
$WorkspaceRoot = Split-Path -Parent $ValheimSourceRoot

$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe"
$UnityProject = Join-Path $WorkspaceRoot "UnityPhysicalOceanValidation"
$UnityPackage = Join-Path $WorkspaceRoot "UnityPhysicalOcean"

$LocalBuildRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\PhysicalWaterUnityBuild"
$LocalUnityProject = Join-Path $LocalBuildRoot "UnityPhysicalOceanValidation"
$LocalUnityPackage = Join-Path $LocalBuildRoot "UnityPhysicalOcean"
$LocalUnityLog = Join-Path $LocalUnityProject "VolumetricStageB1ValidationUnity.log"
$LocalReport = Join-Path $LocalUnityProject "VolumetricStageB1Validation.log"
$DriveUnityLog = Join-Path $UnityProject "VolumetricStageB1ValidationUnity.log"
$DriveReport = Join-Path $UnityProject "VolumetricStageB1Validation.log"

Write-Host ""
Write-Host " PhysicalWater 0.6.0-devB6 - STAGE-B1 LONG-RUN / DENSITY VALIDATION" -ForegroundColor Cyan
Write-Host ""
Write-Host "Workspace root: $WorkspaceRoot" -ForegroundColor DarkGray
Write-Host "Validation source: $UnityProject" -ForegroundColor DarkGray
Write-Host "Package source: $UnityPackage" -ForegroundColor DarkGray
Write-Host "Local NTFS cache: $LocalBuildRoot" -ForegroundColor DarkGray

if (!(Test-Path -LiteralPath $UnityExe -PathType Leaf)) { throw "Missing Unity 6000.0.61f1 editor: $UnityExe" }
if (!(Test-Path -LiteralPath $UnityProject -PathType Container)) { throw "Missing validation project: $UnityProject" }
if (!(Test-Path -LiteralPath $UnityPackage -PathType Container)) { throw "Missing UnityPhysicalOcean package: $UnityPackage" }

$required = @(
    (Join-Path $UnityPackage "Runtime\VolumetricWaterSettings.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricWaterDiagnostics.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricWaterDomain.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricFlipSettings.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricFlipDiagnostics.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricFlipDensityDiagnostics.cs"),
    (Join-Path $UnityPackage "Runtime\VolumetricFlipDomain.cs"),
    (Join-Path $UnityPackage "Runtime\Shaders\PhysicalVolumetricWater.compute"),
    (Join-Path $UnityPackage "Runtime\Shaders\PhysicalVolumetricFlip.compute"),
    (Join-Path $UnityProject "Assets\Editor\RunVolumetricStageB1Validation.cs")
)
foreach ($path in $required) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing mandatory Stage-B1 validation input: $path" }
}

Write-Host "Preparing local Unity Stage-B1 validation workspace..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $LocalBuildRoot | Out-Null
New-Item -ItemType Directory -Force -Path $LocalUnityProject | Out-Null

foreach ($folder in @("Assets", "Packages", "ProjectSettings")) {
    $src = Join-Path $UnityProject $folder
    $dst = Join-Path $LocalUnityProject $folder
    if (!(Test-Path -LiteralPath $src -PathType Container)) { throw "Missing validation folder: $src" }
    if (Test-Path -LiteralPath $dst) { Remove-Item -LiteralPath $dst -Recurse -Force }
    Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
}

if (Test-Path -LiteralPath $LocalUnityPackage) { Remove-Item -LiteralPath $LocalUnityPackage -Recurse -Force }
Copy-Item -LiteralPath $UnityPackage -Destination $LocalUnityPackage -Recurse -Force

$LocalManifestPath = Join-Path $LocalUnityProject "Packages\manifest.json"
$manifestText = [System.IO.File]::ReadAllText($LocalManifestPath).TrimStart([char]0xFEFF)
$manifest = $manifestText | ConvertFrom-Json
$packageUri = "file:" + ($LocalUnityPackage -replace "\\", "/")
$manifest.dependencies.'com.r4v9n1.physical-ocean' = $packageUri
$manifestJson = $manifest | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($LocalManifestPath, $manifestJson, (New-Object System.Text.UTF8Encoding($false)))

foreach ($file in @($LocalUnityLog, $LocalReport, $DriveUnityLog, $DriveReport)) {
    if (Test-Path -LiteralPath $file -PathType Leaf) { Remove-Item -LiteralPath $file -Force }
}

Write-Host "Running Unity Stage-B1 long-run/density validation..." -ForegroundColor Cyan
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $UnityExe
$psi.Arguments = "-batchmode -force-d3d11 -quit -projectPath `"$LocalUnityProject`" -executeMethod RunVolumetricStageB1Validation.Run -logFile `"$LocalUnityLog`""
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$p = New-Object System.Diagnostics.Process
$p.StartInfo = $psi
if (!$p.Start()) { throw "Failed to launch Unity: $UnityExe" }
$p.WaitForExit()
$exitCode = $p.ExitCode

if (Test-Path -LiteralPath $LocalUnityLog -PathType Leaf) { Copy-Item $LocalUnityLog $DriveUnityLog -Force }
if (Test-Path -LiteralPath $LocalReport -PathType Leaf) { Copy-Item $LocalReport $DriveReport -Force }

if ($exitCode -ne 0) {
    $hint = if (Test-Path -LiteralPath $DriveUnityLog -PathType Leaf) { " See $DriveUnityLog" } else { "" }
    throw "Volumetric Stage-B1 Unity validation failed with exit code $exitCode.$hint"
}
if (!(Test-Path -LiteralPath $DriveReport -PathType Leaf)) { throw "Unity exited successfully but did not produce $DriveReport" }

$reportText = Get-Content -LiteralPath $DriveReport -Raw
Write-Host ""
Write-Host $reportText
if ($reportText -notmatch "RESULT:\s+PASS") { throw "Volumetric Stage-B1 report did not contain PASS. See $DriveReport" }
Write-Host "Volumetric Stage-B1 validation PASSED." -ForegroundColor Green
Write-Host "Report: $DriveReport" -ForegroundColor Green
Write-Host "Unity log: $DriveUnityLog" -ForegroundColor DarkGray
