param(
    [string]$ValheimDir = $env:VALHEIM_DIR,
    [string]$GameManagedDir = $env:VALHEIM_MANAGED_DIR,
    [string]$BepInExCoreDir = $env:BEPINEX_CORE_DIR,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-ValheimDir {
    param([string]$Requested)

    if ($Requested -and (Test-Path -LiteralPath $Requested)) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $candidates = @(
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server",
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim Dedicated Server",
        "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
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
        (Join-Path $Root "valheim_server_Data\Managed"),
        (Join-Path $Root "valheim_Data\Managed")
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "assembly_valheim.dll")) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw "Could not find Valheim managed assemblies. Pass -GameManagedDir or set VALHEIM_MANAGED_DIR."
}

function Ensure-BepInExRefs {
    param([string]$Requested, [string]$ValheimRoot)

    if ($Requested -and
        (Test-Path -LiteralPath (Join-Path $Requested "BepInEx.dll")) -and
        (Test-Path -LiteralPath (Join-Path $Requested "0Harmony.dll"))) {
        return (Resolve-Path -LiteralPath $Requested).Path
    }

    $serverCore = Join-Path $ValheimRoot "BepInEx\core"
    if ((Test-Path -LiteralPath (Join-Path $serverCore "BepInEx.dll")) -and
        (Test-Path -LiteralPath (Join-Path $serverCore "0Harmony.dll"))) {
        return (Resolve-Path -LiteralPath $serverCore).Path
    }

    $refsRoot = Join-Path $ProjectRoot ".refs"
    $refsDir = Join-Path $refsRoot "bepinex-5.4.23.3"
    $bepInExDll = Join-Path $refsDir "BepInEx.dll"
    $harmonyDll = Join-Path $refsDir "0Harmony.dll"

    if (!(Test-Path -LiteralPath $bepInExDll) -or !(Test-Path -LiteralPath $harmonyDll)) {
        New-Item -ItemType Directory -Force -Path $refsRoot | Out-Null
        $packageUrl = "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.3/BepInEx_win_x64_5.4.23.3.zip"
        $packageSha256 = "41A089E5B1B1F0713B331346BAF6677B1184C69EABEBF51101097954E854C749"
        $packagePath = Join-Path $refsRoot "BepInEx_win_x64_5.4.23.3.zip"
        $extractPath = Join-Path $refsRoot "BepInEx_win_x64_5.4.23.3"

        Write-Host "BepInEx refs were not found locally; downloading compile-time references..."
        Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath
        $actualSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash
        if ($actualSha256 -ne $packageSha256) {
            throw "Downloaded BepInEx archive checksum did not match the pinned 5.4.23.3 release."
        }
        Expand-Archive -LiteralPath $packagePath -DestinationPath $extractPath -Force

        New-Item -ItemType Directory -Force -Path $refsDir | Out-Null
        Copy-Item -LiteralPath (Join-Path $extractPath "BepInEx\core\BepInEx.dll") -Destination $bepInExDll -Force
        Copy-Item -LiteralPath (Join-Path $extractPath "BepInEx\core\0Harmony.dll") -Destination $harmonyDll -Force
    }

    Write-Warning "Using downloaded compile-time BepInEx references. The server must still have BepInEx installed at runtime."
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

$distDir = Join-Path $ProjectRoot "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdkList = ""
if ($dotnet) {
    $sdkList = & dotnet --list-sdks
}

if ($sdkList) {
    dotnet build (Join-Path $ProjectRoot "TerramizerServer.csproj") `
        -c $Configuration `
        -p:ValheimDir="$ValheimDir" `
        -p:GameManagedDir="$GameManagedDir" `
        -p:BepInExCoreDir="$BepInExCoreDir" `
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
        (Join-Path $GameManagedDir "netstandard.dll"),
        (Join-Path $GameManagedDir "assembly_valheim.dll"),
        (Join-Path $GameManagedDir "assembly_utils.dll"),
        (Join-Path $GameManagedDir "UnityEngine.dll"),
        (Join-Path $GameManagedDir "UnityEngine.CoreModule.dll"),
        (Join-Path $GameManagedDir "UnityEngine.PhysicsModule.dll")
    )

    foreach ($ref in $refs) {
        if (!(Test-Path -LiteralPath $ref)) {
            throw "Missing reference: $ref"
        }
    }

    $outPath = Join-Path $distDir "TerramizerServer.dll"
    $refArgs = $refs | ForEach-Object { "/reference:$_" }
    & $csc /nologo /target:library /optimize+ "/out:$outPath" $refArgs $sources
    if ($LASTEXITCODE -ne 0) {
        throw "csc.exe failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Built: $(Join-Path $distDir 'TerramizerServer.dll')"
