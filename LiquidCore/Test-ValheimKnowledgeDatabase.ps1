param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)

$ErrorActionPreference = "Stop"
$databasePath = Join-Path $PSScriptRoot "knowledge\valheim-knowledge-v1.json"
$database = Get-Content -LiteralPath $databasePath -Raw | ConvertFrom-Json
$assemblyPath = Join-Path $ValheimDir "valheim_Data\Managed\assembly_valheim.dll"
$manifestPath = Join-Path (Split-Path -Parent (Split-Path -Parent $ValheimDir)) "appmanifest_892970.acf"
$pluginRoot = Join-Path $ValheimDir "BepInEx\plugins"
$builtDll = Join-Path $env:LOCALAPPDATA "R4V9N1\LiquidCore\dist\LiquidCore.dll"

if ($database.schemaVersion -ne 1) { throw "Knowledge schema mismatch." }
if (!(Test-Path -LiteralPath $assemblyPath -PathType Leaf)) { throw "Valheim assembly missing." }
$assemblyHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash.ToLowerInvariant()
if ($assemblyHash -ne $database.valheim.assemblySha256) { throw "Valheim assembly fingerprint mismatch." }

$manifest = Get-Content -LiteralPath $manifestPath -Raw
$buildMatch = [regex]::Match($manifest, '"buildid"\s+"(?<id>\d+)"')
if (!$buildMatch.Success -or $buildMatch.Groups['id'].Value -ne $database.valheim.steamBuildId) {
    throw "Steam build fingerprint mismatch."
}

$pluginRows = @(Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -File -Recurse |
    Where-Object { $_.Name -ne 'LiquidCore.dll' } |
    ForEach-Object {
        $relative = $_.FullName.Substring($pluginRoot.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        "$relative|$($_.Length)|$hash"
    })
[Array]::Sort($pluginRows, [StringComparer]::Ordinal)
$canonicalModSet = $pluginRows -join "`n"
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $modFingerprint = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonicalModSet)))).Replace('-', '').ToLowerInvariant()
} finally { $sha.Dispose() }
if ($modFingerprint -ne $database.modSet.fingerprint) { throw "Mod-set fingerprint mismatch." }

$terrainRule = $database.signalRules | Where-Object eventLabel -eq 'heightmap terrain operation/regenerate'
if ($null -eq $terrainRule -or $terrainRule.authority -ne 'authoritative-local' -or !$terrainRule.immediate -or $terrainRule.discoveryFallback) {
    throw "Authoritative terrain rule is missing or unsafe."
}

$adapterSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\PhysicalWaterValheimWorldGeometryAdapter.cs') -Raw
$databaseSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\ValheimKnowledgeDatabase.cs') -Raw
if (!$databaseSource.Contains('Paths.GameRootPath') -or
    !$databaseSource.Contains('"assembly_valheim.dll"') -or
    $databaseSource.Contains('HashFile(typeof(TerrainComp).Assembly.Location)')) {
    throw "Runtime database validity must use the immutable installed-game assembly fingerprint."
}
foreach ($requiredCertificationText in @(
    'DataContractJsonSerializer',
    'MATCH schema=',
    'database-first serving active',
    'bypass runtime reinspection on hit',
    'terrainRules=',
    'bypass discovery')) {
    if (!$databaseSource.Contains($requiredCertificationText)) {
        throw "Startup database certification is incomplete: $requiredCertificationText"
    }
}
$fastPathIndex = $adapterSource.IndexOf('record.AuthoritativeLocalSignal && cached != null', [StringComparison]::Ordinal)
$rediscoveryIndex = $adapterSource.IndexOf('Source source = CreateSource(record.Root);', [StringComparison]::Ordinal)
if ($fastPathIndex -lt 0 -or $rediscoveryIndex -lt 0 -or $fastPathIndex -gt $rediscoveryIndex) {
    throw "Known local terrain path does not precede runtime rediscovery."
}
foreach ($required in @('knowledge-authoritative-local-hit', 'EstimateDirtyRegion(record.NewBounds', 'record.OldBounds', 'record.NewBounds')) {
    if (!$adapterSource.Contains($required)) { throw "Terrain fast-path contract missing: $required" }
}

if (!(Test-Path -LiteralPath $builtDll -PathType Leaf)) { throw "Build output is missing: $builtDll" }
$assembly = [Reflection.Assembly]::LoadFile($builtDll)
$resources = @($assembly.GetManifestResourceNames())
if ('PhysicalWater.knowledge.valheim-knowledge-v1.json' -notin $resources) { throw "Database is not embedded in LiquidCore.dll." }

# Compare the exact failed-live full-root shape with a normal 2 m terrain operation.
# This is a signal/apply footprint contract, not a solver-physics approximation.
$cellSize = 0.75
$padding = 4
function Axis-Cells([double]$size) { [math]::Ceiling($size / $cellSize) + 2 * $padding + 1 }
$fullRootCells = 221184
$localCells = (Axis-Cells 4.0) * (Axis-Cells 16.0) * (Axis-Cells 4.0)
$reduction = $fullRootCells / [double]$localCells
if ($reduction -lt 20.0) { throw "Localized terrain footprint reduction is insufficient: $reduction x" }

$signals = @{}
foreach ($rule in $database.signalRules) { $signals[$rule.eventLabel] = $rule }
$watch = [Diagnostics.Stopwatch]::StartNew()
for ($i = 0; $i -lt 100000; $i++) { $null = $signals['heightmap terrain operation/regenerate'] }
$watch.Stop()
$lookupNanoseconds = $watch.Elapsed.TotalMilliseconds * 1000000.0 / 100000.0

[pscustomobject]@{
    Result = 'PASS'
    Schema = $database.schemaVersion
    SteamBuild = $database.valheim.steamBuildId
    AssemblyHash = $assemblyHash
    ModSetFingerprint = $modFingerprint
    TypeRules = @($database.typeRules).Count
    SignalRules = @($database.signalRules).Count
    ObservedAssets = @($database.observedAssets).Count
    EmbeddedResource = $true
    TerrainFastPathBeforeRediscovery = $true
    FailedLiveFullRootCells = $fullRootCells
    CandidateLocalCells = $localCells
    DirtyCellReduction = [math]::Round($reduction, 2)
    MeanSignalLookupNanoseconds = [math]::Round($lookupNanoseconds, 1)
} | Format-List
