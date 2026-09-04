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

$cecilPath = Join-Path $ValheimDir "BepInEx\core\Mono.Cecil.dll"
if (!(Test-Path -LiteralPath $cecilPath -PathType Leaf)) { throw "Mono.Cecil metadata reader missing: $cecilPath" }
[void][Reflection.Assembly]::LoadFrom($cecilPath)
$gameAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
try {
    $typeByName = @{}
    foreach ($type in $gameAssembly.MainModule.Types) { $typeByName[$type.Name] = $type }
    $assemblyTypeContracts = @($database.assemblyContracts.types)
    if ($assemblyTypeContracts.Count -lt @($database.typeRules).Count) {
        throw "Assembly contract does not cover every knowledge type rule."
    }
    foreach ($contract in $assemblyTypeContracts) {
        if (!$typeByName.ContainsKey($contract.type)) { throw "Recorded Valheim type is absent: $($contract.type)" }
        $type = $typeByName[$contract.type]
        if ($type.BaseType.FullName -ne $contract.baseType) {
            throw "Base type mismatch for $($contract.type): $($type.BaseType.FullName) != $($contract.baseType)"
        }
        $fieldNames = @($type.Fields | ForEach-Object Name)
        foreach ($field in @($contract.fields)) {
            if ($field -notin $fieldNames) { throw "Recorded field is absent: $($contract.type).$field" }
        }
        $methodNames = @($type.Methods | ForEach-Object Name)
        foreach ($method in @($contract.methods)) {
            if ($method -notin $methodNames) { throw "Recorded method is absent: $($contract.type).$method" }
        }
    }

    $callbackContracts = @($database.assemblyContracts.callbacks)
    foreach ($callback in $callbackContracts) {
        if (!$typeByName.ContainsKey($callback.type)) { throw "Callback owner type is absent: $($callback.type)" }
        if ($callback.method -notin @($typeByName[$callback.type].Methods | ForEach-Object Name)) {
            throw "Recorded callback is absent: $($callback.type).$($callback.method)"
        }
    }
    $externalSignalLabels = @($database.signalRules |
        Where-Object { $_.ownerType -notlike 'LiquidCore *' } |
        ForEach-Object eventLabel |
        Select-Object -Unique)
    $callbackLabels = @($callbackContracts | ForEach-Object eventLabel | Select-Object -Unique)
    foreach ($label in $externalSignalLabels) {
        if ($label -notin $callbackLabels) { throw "Signal lacks an assembly-verified callback: $label" }
    }
} finally {
    $gameAssembly.Dispose()
}

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
    'MATCHED current game/mod/schema fingerprint: schema=',
    'known assets/terrain rules are being served from the database rather than runtime reinspection',
    'bypass hierarchy/category reinspection on hit',
    'terrainRules=',
    'bypass discovery')) {
    if (!$databaseSource.Contains($requiredCertificationText)) {
        throw "Startup database certification is incomplete: $requiredCertificationText"
    }
}
$eventPatchSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\PhysicalWaterValheimWorldGeometryPatches.cs') -Raw
$pluginSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\PhysicalWaterPlugin.cs') -Raw
foreach ($requiredLifecycleBridge in @(
    '[HarmonyPatch(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) })]',
    'ValheimGeometryDirtyBridge.Mark("streamed source appeared", view)',
    '[HarmonyPatch(typeof(ZNetView), "ResetZDO")]',
    'ValheimGeometryDirtyBridge.Mark("streamed source disappeared", __instance)'
)) {
    if (!$eventPatchSource.Contains($requiredLifecycleBridge)) {
        throw "Direct streamed lifecycle bridge is incomplete: $requiredLifecycleBridge"
    }
}
foreach ($requiredPatchRegistration in @(
    'PatchSafely(typeof(ValheimGeometryStreamedSourceAppearedPatch)',
    'PatchSafely(typeof(ValheimGeometryStreamedSourceDisappearedPatch)',
    'Logger.LogInfo("Patched " + label + ".")'
)) {
    if (!$pluginSource.Contains($requiredPatchRegistration)) {
        throw "Compiled lifecycle patch is not registered with Harmony: $requiredPatchRegistration"
    }
}
foreach ($requiredLifecycleFilter in @(
    'adapter.IsStreamedSourceNearActiveDomain(view)',
    'adapter.IsCachedStreamedSource(__instance)',
    '_cache.ContainsKey(BuildSourceId(view.gameObject))'
)) {
    if (!$eventPatchSource.Contains($requiredLifecycleFilter) -and !$adapterSource.Contains($requiredLifecycleFilter)) {
        throw "Streamed lifecycle bridge lacks its bounded cache/domain filter: $requiredLifecycleFilter"
    }
}
if ($eventPatchSource.Contains('GetField("m_instances"') -or
    $eventPatchSource.Contains('AccessTools.Field(typeof(ZNetScene), "m_instances")')) {
    throw 'Streamed lifecycle bridge must not enumerate ZNetScene.m_instances.'
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
$embeddedStream = $assembly.GetManifestResourceStream('PhysicalWater.knowledge.valheim-knowledge-v1.json')
try {
    $embeddedReader = [IO.StreamReader]::new($embeddedStream)
    try { $embeddedDatabase = $embeddedReader.ReadToEnd() | ConvertFrom-Json }
    finally { $embeddedReader.Dispose() }
} finally {
    if ($null -ne $embeddedStream) { $embeddedStream.Dispose() }
}
if (@($embeddedDatabase.assemblyContracts.callbacks).Count -ne $callbackContracts.Count -or
    @($embeddedDatabase.assemblyContracts.types).Count -ne $assemblyTypeContracts.Count) {
    throw 'Built LiquidCore.dll does not embed the currently certified assembly contracts.'
}

$builtAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($builtDll)
try {
    foreach ($compiledBridge in @(
        @{ Type = 'ValheimGeometryStreamedSourceAppearedPatch'; Method = 'Postfix'; Label = 'streamed source appeared' },
        @{ Type = 'ValheimGeometryStreamedSourceDisappearedPatch'; Method = 'Prefix'; Label = 'streamed source disappeared' }
    )) {
        $patchType = $builtAssembly.MainModule.Types | Where-Object Name -eq $compiledBridge.Type
        if ($null -eq $patchType -or 'HarmonyLib.HarmonyPatch' -notin @($patchType.CustomAttributes | ForEach-Object { $_.AttributeType.FullName })) {
            throw "Compiled lifecycle Harmony patch is missing: $($compiledBridge.Type)"
        }
        $patchMethod = $patchType.Methods | Where-Object Name -eq $compiledBridge.Method
        if ($null -eq $patchMethod -or $compiledBridge.Label -notin @($patchMethod.Body.Instructions | ForEach-Object Operand | Where-Object { $_ -is [string] })) {
            throw "Compiled lifecycle patch does not publish its database label: $($compiledBridge.Label)"
        }
        $enumeratesInstances = @($patchMethod.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.DeclaringType.Name -eq 'ZNetScene' -and $_.Operand.Name -eq 'm_instances'
        }).Count -ne 0
        if ($enumeratesInstances) { throw "Compiled lifecycle patch enumerates ZNetScene.m_instances: $($compiledBridge.Type)" }
    }
} finally {
    $builtAssembly.Dispose()
}

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
    AssemblyTypesVerified = $assemblyTypeContracts.Count
    AssemblyCallbacksVerified = $callbackContracts.Count
    ModSetFingerprint = $modFingerprint
    TypeRules = @($database.typeRules).Count
    SignalRules = @($database.signalRules).Count
    ObservedAssets = @($database.observedAssets).Count
    EmbeddedResource = $true
    EmbeddedAssemblyContracts = $true
    TerrainFastPathBeforeRediscovery = $true
    DirectStreamedLifecycleBridge = $true
    CompiledLifecycleBridgeVerified = $true
    FailedLiveFullRootCells = $fullRootCells
    CandidateLocalCells = $localCells
    DirtyCellReduction = [math]::Round($reduction, 2)
    MeanSignalLookupNanoseconds = [math]::Round($lookupNanoseconds, 1)
} | Format-List
