param(
    [string]$ValheimDir = $env:VALHEIM_DIR,
    [string]$GameManagedDir = $env:VALHEIM_MANAGED_DIR,
    [string]$BepInExCoreDir = $env:BEPINEX_CORE_DIR,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$LocalProjectRoot = Join-Path $env:LOCALAPPDATA "R4V9N1\EquipmentSheet"
$ObjDir = Join-Path $LocalProjectRoot "obj"
$DistDir = Join-Path $LocalProjectRoot "dist"

function Find-ValheimDir {
    param([string]$Requested)

    if ($Requested -and (Test-Path -LiteralPath $Requested)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server",
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim Dedicated Server"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Could not find Valheim. Pass -ValheimDir or set VALHEIM_DIR."
}

function Find-ManagedDir {
    param([string]$Root, [string]$Requested)

    if ($Requested -and (Test-Path -LiteralPath $Requested)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $candidates = @(
        (Join-Path $Root "valheim_Data\Managed"),
        (Join-Path $Root "valheim_server_Data\Managed")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "assembly_valheim.dll")) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Could not find Valheim managed assemblies. Pass -GameManagedDir or set VALHEIM_MANAGED_DIR."
}

function Expand-Zip {
    param([string]$ZipPath, [string]$Destination)

    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $Destination -Force
}

function Ensure-BepInExRefs {
    param([string]$Requested, [string]$ValheimRoot)

    if ($Requested -and
        (Test-Path -LiteralPath (Join-Path $Requested "BepInEx.dll")) -and
        (Test-Path -LiteralPath (Join-Path $Requested "0Harmony.dll"))) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $localCore = Join-Path $ValheimRoot "BepInEx\core"
    if ((Test-Path -LiteralPath (Join-Path $localCore "BepInEx.dll")) -and
        (Test-Path -LiteralPath (Join-Path $localCore "0Harmony.dll"))) {
        return (Resolve-Path -LiteralPath $localCore).Path
    }

$refsRoot = Join-Path $LocalProjectRoot "refs"
    $refsDir = Join-Path $refsRoot "bepinex-5.4.22"
    $bepInExDll = Join-Path $refsDir "BepInEx.dll"
    $harmonyDll = Join-Path $refsDir "0Harmony.dll"

    if (!(Test-Path -LiteralPath $bepInExDll) -or !(Test-Path -LiteralPath $harmonyDll)) {
        New-Item -ItemType Directory -Force -Path $refsRoot | Out-Null
        $packageUrl = "https://api.nuget.org/v3-flatcontainer/talespire.bepinex/5.4.22/talespire.bepinex.5.4.22.nupkg"
        $packagePath = Join-Path $refsRoot "talespire.bepinex.5.4.22.nupkg"
        $zipPath = Join-Path $refsRoot "talespire.bepinex.5.4.22.zip"
        $extractPath = Join-Path $refsRoot "talespire.bepinex.5.4.22"

        Write-Host "BepInEx refs were not found locally; downloading compile-time references..."
        Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath
        Copy-Item -LiteralPath $packagePath -Destination $zipPath -Force
        Expand-Zip -ZipPath $zipPath -Destination $extractPath

        New-Item -ItemType Directory -Force -Path $refsDir | Out-Null
        Copy-Item -LiteralPath (Join-Path $extractPath "lib\BepInEx.dll") -Destination $bepInExDll -Force
        Copy-Item -LiteralPath (Join-Path $extractPath "lib\0Harmony.dll") -Destination $harmonyDll -Force
    }

    Write-Warning "Using downloaded compile-time BepInEx references. Your game must still have BepInEx installed at runtime."
    return $refsDir
}

function Get-CscPath {
    $candidate = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    $candidate = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    throw "Could not find csc.exe and no .NET SDK is available."
}

$ValheimDir = Find-ValheimDir -Requested $ValheimDir
$GameManagedDir = Find-ManagedDir -Root $ValheimDir -Requested $GameManagedDir
$BepInExCoreDir = Ensure-BepInExRefs -Requested $BepInExCoreDir -ValheimRoot $ValheimDir

New-Item -ItemType Directory -Force -Path $ObjDir, $DistDir | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdkList = ""
if ($dotnet) {
    $sdkList = & dotnet --list-sdks
}

if ($sdkList) {
    dotnet build (Join-Path $ProjectRoot "EquipmentSheet.csproj") `
        -c $Configuration `
        -p:ValheimDir="$ValheimDir" `
        -p:GameManagedDir="$GameManagedDir" `
        -p:BepInExCoreDir="$BepInExCoreDir" `
        -p:BaseIntermediateOutputPath="$ObjDir\" `
        -p:GenerateTargetFrameworkAttribute=false `
        -p:OutputPath="$distDir\"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}
else {
    $csc = Get-CscPath
    $sources = Get-ChildItem -LiteralPath (Join-Path $ProjectRoot "src") -Filter "*.cs" -Recurse | ForEach-Object { $_.FullName }
    $refs = @(
        (Join-Path $BepInExCoreDir "BepInEx.dll"),
        (Join-Path $BepInExCoreDir "0Harmony.dll"),
        (Join-Path $GameManagedDir "assembly_valheim.dll"),
        (Join-Path $GameManagedDir "assembly_guiutils.dll"),
        (Join-Path $GameManagedDir "assembly_utils.dll"),
        (Join-Path $GameManagedDir "netstandard.dll"),
        (Join-Path $GameManagedDir "UnityEngine.dll"),
        (Join-Path $GameManagedDir "UnityEngine.CoreModule.dll"),
        (Join-Path $GameManagedDir "UnityEngine.TextRenderingModule.dll"),
        (Join-Path $GameManagedDir "UnityEngine.UI.dll"),
        (Join-Path $GameManagedDir "UnityEngine.UIModule.dll"),
        (Join-Path $GameManagedDir "Unity.TextMeshPro.dll")
    )

    foreach ($ref in $refs) {
        if (!(Test-Path -LiteralPath $ref)) {
            throw "Missing reference: $ref"
        }
    }

    $outPath = Join-Path $distDir "EquipmentSheet.dll"
    $refArgs = $refs | ForEach-Object { "/reference:$_" }
    & $csc /nologo /target:library /optimize+ "/out:$outPath" $refArgs $sources
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Built: $(Join-Path $distDir 'EquipmentSheet.dll')"
