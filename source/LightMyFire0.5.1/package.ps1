param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Owner = "R4V9N1"
$PackageName = "LightMyFire_Coal_Resin"
$Version = "0.5.1"
$ThunderstoreDir = Join-Path $ProjectRoot "thunderstore"
$DllPath = Join-Path $ProjectRoot "dist\LightMyFire.dll"
$ArtifactDir = Join-Path $ProjectRoot "artifacts"
$StageDir = Join-Path $ProjectRoot "dist\package\$Owner-$PackageName-$Version"
$ZipPath = Join-Path $ArtifactDir "$Owner-$PackageName-$Version.zip"

if (!$NoBuild) {
    & (Join-Path $ProjectRoot "build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$manifest = Get-Content -LiteralPath (Join-Path $ThunderstoreDir "manifest.json") -Raw | ConvertFrom-Json
if ($manifest.version_number -ne $Version) { throw "Manifest version does not match $Version." }

$dllVersion = ([Reflection.AssemblyName]::GetAssemblyName($DllPath)).Version.ToString()
if ($dllVersion -ne "$Version.0") { throw "DLL version $dllVersion does not match $Version." }

$pluginSource = Get-Content -LiteralPath (Join-Path $ProjectRoot "src\LightMyFirePlugin.cs") -Raw
if ($pluginSource -notmatch 'PluginGuid = "r4v9n1\.lightmyfire"') { throw "Plugin GUID/config filename is incorrect." }
if ($pluginSource -notmatch 'PluginVersion = "0\.5\.1"') { throw "Plugin source version is not 0.5.1." }

$AssetBundlePath = Join-Path $ProjectRoot "assets\lightmyfire_assets"
$ExpectedAssetSha256 = "c59d850aad2b444cf1dac1b2e700060cfb37182cbf4532372d619a06743a9138"
$assetHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $AssetBundlePath).Hash.ToLowerInvariant()
if ($assetHash -ne $ExpectedAssetSha256) { throw "Production AssetBundle hash mismatch; refusing to package." }

$allSourceFiles = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "src") -Filter "*.cs" -Recurse
foreach ($file in $allSourceFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($forbidden in @("DestroyZDO", "ZNetScene.instance.Destroy", "RemoveAll", "TerrainModifier", "Heightmap")) {
        if ($content.Contains($forbidden)) { throw "Forbidden destructive/world hook found in $($file.Name): $forbidden" }
    }
}

if (Test-Path -LiteralPath $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }

New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "plugins\LightMyFire") | Out-Null
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null

Copy-Item -LiteralPath $DllPath -Destination (Join-Path $StageDir "plugins\LightMyFire\LightMyFire.dll")
$StagedDll = Join-Path $StageDir "plugins\LightMyFire\LightMyFire.dll"
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $DllPath).Hash -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $StagedDll).Hash) {
    throw "Staged package DLL does not match the freshly built DLL."
}
foreach ($name in @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")) {
    $path = Join-Path $ThunderstoreDir $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Thunderstore package file: $path" }
    Copy-Item -LiteralPath $path -Destination $StageDir
}

foreach ($name in @("manifest.json", "README.md", "icon.png")) {
    if (!(Test-Path -LiteralPath (Join-Path $StageDir $name) -PathType Leaf)) {
        throw "Thunderstore root file missing after staging: $name"
    }
}

# Create ZIP manually. ZipArchive entry names are explicitly normalized to forward slashes,
# avoiding Windows backslash entries such as plugins\LightMyFire\LightMyFire.dll.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open(
    $ZipPath,
    [System.IO.Compression.ZipArchiveMode]::Create
)
try {
    $files = Get-ChildItem -LiteralPath $StageDir -File -Recurse
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($StageDir.Length).TrimStart('\','/')
        $entryName = $relative.Replace('\','/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip,
            $file.FullName,
            $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

# Validate the actual archive entries before declaring success.
$check = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $names = @($check.Entries | ForEach-Object { $_.FullName })
    foreach ($required in @(
        "manifest.json",
        "README.md",
        "CHANGELOG.md",
        "icon.png",
        "plugins/LightMyFire/LightMyFire.dll"
    )) {
        if ($names -notcontains $required) { throw "Thunderstore ZIP missing required entry: $required" }
    }
    foreach ($name in $names) {
        if ($name.Contains("\")) { throw "Thunderstore ZIP contains a Windows backslash entry: $name" }
    }
}
finally {
    $check.Dispose()
}

Write-Host ""
Write-Host "Thunderstore package validated." -ForegroundColor Green
Write-Host "Owner/team: $Owner"
Write-Host "Version: $Version"
Write-Host "Config GUID: r4v9n1.lightmyfire"
Write-Host "UPLOAD THIS FILE UNCHANGED:" -ForegroundColor Cyan
Write-Host "  $ZipPath" -ForegroundColor Cyan
Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath, $DllPath | ForEach-Object { Write-Host "$($_.Hash)  $($_.Path)" }
