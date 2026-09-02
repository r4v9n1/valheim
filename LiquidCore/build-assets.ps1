param(
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ValheimRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $ProjectRoot))
$WorkspaceRoot = Join-Path $ValheimRoot "workspace\LiquidCore"
$UnityProject = Join-Path $WorkspaceRoot "UnityPhysicalOceanValidation"
$UnityPackage = Join-Path $WorkspaceRoot "UnityPhysicalOcean"
$LocalBuildRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore\UnityAssetBuild"
$LocalUnityProject = Join-Path $LocalBuildRoot "UnityPhysicalOceanValidation"
$LocalUnityPackage = Join-Path $LocalBuildRoot "UnityPhysicalOcean"
$LocalOutputDir = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore\dist"
$LocalBundlePath = Join-Path $LocalOutputDir "physicalwater_assets"
$LocalUnityLog = Join-Path $LocalBuildRoot "BuildPhysicalWaterBundle.log"

foreach ($required in @($UnityExe, $UnityProject, $UnityPackage)) {
    if (!(Test-Path -LiteralPath $required)) {
        throw "Missing required AssetBundle build input: $required"
    }
}

New-Item -ItemType Directory -Force -Path $LocalUnityProject, $LocalOutputDir | Out-Null

# Keep Unity's high-churn Library on local NTFS, but refresh every authoritative
# source/build-input folder on each run.
foreach ($folder in @("Assets", "Packages", "ProjectSettings")) {
    $source = Join-Path $UnityProject $folder
    $destination = Join-Path $LocalUnityProject $folder
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
}
if (Test-Path -LiteralPath $LocalUnityPackage) {
    Remove-Item -LiteralPath $LocalUnityPackage -Recurse -Force
}
Copy-Item -LiteralPath $UnityPackage -Destination $LocalUnityPackage -Recurse -Force

$manifestPath = Join-Path $LocalUnityProject "Packages\manifest.json"
$manifest = ([IO.File]::ReadAllText($manifestPath).TrimStart([char]0xFEFF) | ConvertFrom-Json)
$manifest.dependencies.'com.r4v9n1.physical-ocean' = "file:" + ($LocalUnityPackage -replace "\\", "/")
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 20),
    [Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $LocalBundlePath) {
    Remove-Item -LiteralPath $LocalBundlePath -Force
}
if (Test-Path -LiteralPath $LocalUnityLog) {
    Remove-Item -LiteralPath $LocalUnityLog -Force
}
$buildStartedUtc = [DateTime]::UtcNow

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $UnityExe
$startInfo.Arguments = "-batchmode -quit -projectPath `"$LocalUnityProject`" -executeMethod BuildPhysicalWaterBundle.Build -logFile `"$LocalUnityLog`""
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.EnvironmentVariables["PHYSICALWATER_DIST_DIR"] = $LocalOutputDir
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
if (!$process.Start()) { throw "Failed to launch Unity: $UnityExe" }
$process.WaitForExit()

if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $LocalBundlePath -PathType Leaf)) {
    throw "Unity AssetBundle build failed (exit $($process.ExitCode)). See $LocalUnityLog"
}
$bundle = Get-Item -LiteralPath $LocalBundlePath
if ($bundle.LastWriteTimeUtc -lt $buildStartedUtc.AddSeconds(-5)) {
    throw "Unity returned a stale AssetBundle: $LocalBundlePath"
}

$hash = (Get-FileHash -LiteralPath $LocalBundlePath -Algorithm SHA256).Hash
Write-Host "Validated fresh LiquidCore AssetBundle: $LocalBundlePath" -ForegroundColor Green
Write-Host "SHA256 $hash"
