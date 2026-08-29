param([switch]$NoBuild)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Owner = "R4V9N1"
$PackageName = "BrennivinProtection"
$Version = "0.1.3"
$ThunderstoreDir = Join-Path $ProjectRoot "thunderstore"
$DllPath = Join-Path $ProjectRoot "dist\BrennivinProtection.dll"
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

if (Test-Path -LiteralPath $StageDir) { Remove-Item -LiteralPath $StageDir -Recurse -Force }
if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }

New-Item -ItemType Directory -Force -Path (Join-Path $StageDir "plugins\BrennivinProtection") | Out-Null
New-Item -ItemType Directory -Force -Path $ArtifactDir | Out-Null

Copy-Item -LiteralPath $DllPath -Destination (Join-Path $StageDir "plugins\BrennivinProtection\BrennivinProtection.dll")
foreach ($name in @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")) {
    $path = Join-Path $ThunderstoreDir $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing Thunderstore package file: $path" }
    Copy-Item -LiteralPath $path -Destination $StageDir
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
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

$check = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
try {
    $names = @($check.Entries | ForEach-Object { $_.FullName })
    foreach ($required in @(
        "manifest.json",
        "README.md",
        "CHANGELOG.md",
        "icon.png",
        "plugins/BrennivinProtection/BrennivinProtection.dll"
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

Write-Host "Thunderstore package validated." -ForegroundColor Green
Write-Host "UPLOAD THIS FILE UNCHANGED:" -ForegroundColor Cyan
Write-Host "  $ZipPath" -ForegroundColor Cyan
Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath, $DllPath | ForEach-Object { Write-Host "$($_.Hash)  $($_.Path)" }
