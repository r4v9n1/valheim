param(
    [switch]$Install,
    [string]$Version = "0.1.5",
    [string]$ValheimDir = ""
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BuildRoot = "G:\My Drive\build"
$AssemblyAuthority = Join-Path $BuildRoot "assembly_valheim.dll"
$ReleaseRoot = Join-Path $BuildRoot "Valheim\releases\HereComesTheVein"
$TempRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\HereComesTheVein"

function Resolve-ValheimDir {
    param([string]$Requested)

    if ($Requested -and (Test-Path -LiteralPath $Requested)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
        "C:\Program Files\Steam\steamapps\common\Valheim",
        "D:\SteamLibrary\steamapps\common\Valheim",
        "E:\SteamLibrary\steamapps\common\Valheim",
        "F:\SteamLibrary\steamapps\common\Valheim"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Valheim install was not found. Re-run with -ValheimDir 'X:\path\to\Valheim'."
}

$ValheimDir = Resolve-ValheimDir -Requested $ValheimDir
$ManagedDir = Join-Path $ValheimDir "valheim_Data\Managed"
$BepInExCoreDir = Join-Path $ValheimDir "BepInEx\core"

$required = @(
    (Join-Path $ManagedDir "UnityEngine.dll"),
    (Join-Path $ManagedDir "UnityEngine.CoreModule.dll"),
    (Join-Path $BepInExCoreDir "BepInEx.dll"),
    (Join-Path $BepInExCoreDir "0Harmony.dll"),
    $AssemblyAuthority
)

foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required build reference is missing: $path"
    }
}

$authorityHash = (Get-FileHash -LiteralPath $AssemblyAuthority -Algorithm SHA256).Hash
Write-Host "Valheim:            $ValheimDir"
Write-Host "Managed refs:       $ManagedDir"
Write-Host "BepInEx refs:       $BepInExCoreDir"
Write-Host "Assembly authority: $AssemblyAuthority"
Write-Host "Assembly SHA256:    $authorityHash"

$installedAssembly = Join-Path $ManagedDir "assembly_valheim.dll"
if (-not (Test-Path -LiteralPath $installedAssembly)) {
    throw "Installed Valheim assembly was not found: $installedAssembly"
}

$installedHash = (Get-FileHash -LiteralPath $installedAssembly -Algorithm SHA256).Hash
Write-Host "Installed SHA256:   $installedHash"

if ($installedHash -ne $authorityHash) {
    throw @"
Valheim assembly mismatch.

Drive authority:
  $AssemblyAuthority
  $authorityHash

Installed game:
  $installedAssembly
  $installedHash

Refresh G:\My Drive\build\assembly_valheim.dll from the currently installed game before building.
"@
}

# Lightweight contract gate against the exact current assembly. This does not
# decompile the DLL and creates no investigation output in Google Drive.
$assemblyAscii = [System.Text.Encoding]::ASCII.GetString(
    [System.IO.File]::ReadAllBytes($AssemblyAuthority)
)

$requiredContracts = @(
    "ZNetScene",
    "CreateObject",
    "MineRock5",
    "MineRock",
    "DropTable",
    "m_dropItems",
    "m_drops",
    "m_item",
    "GetPrefab"
)

$missingContracts = @(
    $requiredContracts | Where-Object { -not $assemblyAscii.Contains($_) }
)

if ($missingContracts.Count -gt 0) {
    throw "Current assembly is missing required HereComesTheVein contract identifiers: $($missingContracts -join ', ')"
}

Write-Host "Contract identifiers: PASS"

$objRoot = Join-Path $TempRoot "obj"
$binRoot = Join-Path $TempRoot "bin"
$stageRoot = Join-Path $TempRoot "package"

foreach ($dir in @($objRoot, $binRoot, $stageRoot)) {
    if (Test-Path -LiteralPath $dir) {
        Remove-Item -LiteralPath $dir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$project = Join-Path $ProjectRoot "HereComesTheVein.csproj"

$buildArgs = @(
    "build",
    $project,
    "-c", "Release",
    "/p:ValheimDir=$ValheimDir",
    "/p:GameManagedDir=$ManagedDir",
    "/p:BepInExCoreDir=$BepInExCoreDir",
    "/p:AssemblyAuthority=$AssemblyAuthority",
    "/p:BaseIntermediateOutputPath=$objRoot\",
    "/p:OutputPath=$binRoot\",
    "/p:AppendTargetFrameworkToOutputPath=false"
)

Write-Host ""
Write-Host "Building HereComesTheVein $Version..."
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $binRoot "HereComesTheVein.dll"
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Build succeeded but DLL was not found at $dll"
}

New-Item -ItemType Directory -Force -Path (Join-Path $stageRoot "plugins\HereComesTheVein") | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $stageRoot "plugins\HereComesTheVein\HereComesTheVein.dll") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "README.md") -Destination (Join-Path $stageRoot "README.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "CHANGELOG.md") -Destination (Join-Path $stageRoot "CHANGELOG.md") -Force
Copy-Item -LiteralPath (Join-Path $ProjectRoot "assets\icon.png") -Destination (Join-Path $stageRoot "icon.png") -Force

$manifest = [ordered]@{
    name = "HereComesTheVein"
    version_number = $Version
    website_url = ""
    description = "Adds weighted IronOre drops to existing copper veins, averaging about 60% CopperOre and 40% IronOre."
    dependencies = @(
        "denikson-BepInExPack_Valheim-5.4.2350"
    )
}

$manifestPath = Join-Path $stageRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

foreach ($requiredName in @("manifest.json", "README.md", "icon.png", "plugins\HereComesTheVein\HereComesTheVein.dll")) {
    $requiredPath = Join-Path $stageRoot $requiredName
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Package staging is missing $requiredName"
    }
}

Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile((Join-Path $stageRoot "icon.png"))
try {
    if ($icon.Width -ne 256 -or $icon.Height -ne 256) {
        throw "Thunderstore icon must be exactly 256x256; found $($icon.Width)x$($icon.Height)."
    }
}
finally {
    $icon.Dispose()
}

New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null
$zipPath = Join-Path $ReleaseRoot "HereComesTheVein-$Version.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    foreach ($requiredName in @("manifest.json", "README.md", "icon.png", "plugins/HereComesTheVein/HereComesTheVein.dll")) {
        if ($entries -notcontains $requiredName) {
            throw "ZIP validation failed: $requiredName is not at the package root."
        }
    }
}
finally {
    $zip.Dispose()
}

if ($Install) {
    $pluginDir = Join-Path $ValheimDir "BepInEx\plugins\HereComesTheVein"
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
    Copy-Item -LiteralPath $dll -Destination (Join-Path $pluginDir "HereComesTheVein.dll") -Force
    Write-Host "Installed test DLL to: $pluginDir"
}

Write-Host ""
Write-Host "PASS"
Write-Host "DLL:     $dll"
Write-Host "Package: $zipPath"
Write-Host "SHA256:  $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"
