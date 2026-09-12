param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$databasePath = Join-Path $root 'knowledge\valheim-knowledge-v1.json'
$knowledgeSourcePath = Join-Path $root 'src\ValheimKnowledgeDatabase.cs'
$pceSourcePath = Join-Path $root 'src\LiquidCorePceRuntime.cs'

$gameDll = Join-Path $ValheimDir 'valheim_Data\Managed\assembly_valheim.dll'
$pluginRoot = Join-Path $ValheimDir 'BepInEx\plugins'

if (!(Test-Path -LiteralPath $databasePath)) {
    throw "Missing knowledge database: $databasePath"
}

if (!(Test-Path -LiteralPath $gameDll)) {
    throw "Missing Valheim assembly: $gameDll"
}

if (!(Test-Path -LiteralPath $pluginRoot)) {
    throw "Missing BepInEx plugin directory: $pluginRoot"
}

$database = Get-Content -LiteralPath $databasePath -Raw | ConvertFrom-Json
$knowledgeSource = Get-Content -LiteralPath $knowledgeSourcePath -Raw
$pceSource = Get-Content -LiteralPath $pceSourcePath -Raw

function Get-StringSha256([string]$Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

Write-Host "`n=== CURRENT RUNTIME IDENTITY ===" -ForegroundColor Cyan

$actualGame = (Get-FileHash -LiteralPath $gameDll -Algorithm SHA256).Hash.ToLowerInvariant()

$pluginRows = @(
    Get-ChildItem -LiteralPath $pluginRoot -Filter '*.dll' -File -Recurse |
    Where-Object {
        $_.Name -ne 'LiquidCore.dll'
    } |
    ForEach-Object {
        $relative = $_.FullName.Substring($pluginRoot.Length + 1).Replace('\','/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

        "$relative|$($_.Length)|$hash"
    }
)

[Array]::Sort($pluginRows, [StringComparer]::Ordinal)

$actualMods = Get-StringSha256 ($pluginRows -join "`n")

$gameMatches = $actualGame -eq $database.valheim.assemblySha256
$modsMatch = $actualMods -eq $database.modSet.fingerprint

Write-Host "Game certified:   $gameMatches"
Write-Host "Mod-set certified:$modsMatch"
Write-Host "Current game:     $actualGame"
Write-Host "Current mod-set:  $actualMods"
Write-Host "Embedded mod-set: $($database.modSet.fingerprint)"

if (!$gameMatches) {
    throw 'This regression requires the installed Valheim assembly to match the certified database.'
}

# The current machine is our real production witness for the mismatch path.
if ($modsMatch) {
    Write-Host "NOTE: installed mod set currently matches; structural fallback contract will still be checked." -ForegroundColor Yellow
}
else {
    Write-Host "LIVE MISMATCH WITNESS: PRESENT" -ForegroundColor Green
}

Write-Host "`n=== CERTIFIED KNOWLEDGE FAIL-CLOSED CONTRACT ===" -ForegroundColor Cyan

foreach ($required in @(
    'CurrentGameBuildFingerprint',
    'CurrentModFingerprint',
    'GameFingerprintMatched',
    'ModSetFingerprintMatched',
    'certified mod-dependent prefab knowledge is disabled',
    'database.Loaded = true'
)) {
    if (!$knowledgeSource.Contains($required)) {
        throw "Missing knowledge-runtime separation contract: $required"
    }
}

$loadedAssignment = $knowledgeSource.IndexOf(
    'database.Loaded = true',
    [StringComparison]::Ordinal)

$modMismatchGuard = $knowledgeSource.IndexOf(
    'if (!database.ModSetFingerprintMatched)',
    [StringComparison]::Ordinal)

if ($modMismatchGuard -lt 0 -or
    $loadedAssignment -lt 0 -or
    $modMismatchGuard -gt $loadedAssignment) {
    throw 'Certified knowledge must reject the stale mod set before Loaded=true.'
}

Write-Host "STALE CERTIFIED PREFAB KNOWLEDGE: FAIL-CLOSED PASS" -ForegroundColor Green

Write-Host "`n=== CODY CURRENT-RUNTIME IDENTITY CONTRACT ===" -ForegroundColor Cyan

$start = $pceSource.IndexOf(
    'private void InitializeCody()',
    [StringComparison]::Ordinal)

$end = $pceSource.IndexOf(
    'private void LoadWorldCodyCache',
    $start,
    [StringComparison]::Ordinal)

if ($start -lt 0 -or $end -lt 0) {
    throw 'Could not isolate InitializeCody().'
}

$initialize = $pceSource.Substring(
    $start,
    $end - $start)

$firstCacheCreation = $initialize.IndexOf(
    '_codyL1 = new CodyCatchmentCache(',
    [StringComparison]::Ordinal)

if ($firstCacheCreation -lt 0) {
    throw 'Could not locate the first CODY cache construction.'
}

$initializationGate = $initialize.Substring(
    0,
    $firstCacheCreation)

# knowledge.Loaded is allowed later for diagnostics such as
# certifiedKnowledge=... . It must never participate in the gate that
# decides whether CODY itself is constructed.
if ($initializationGate.Contains('knowledge.Loaded')) {
    throw 'CODY construction still depends on certified knowledge.Loaded.'
}

foreach ($required in @(
    'knowledge?.CurrentGameBuildFingerprint',
    'knowledge?.CurrentModFingerprint',
    'new CodyCatchmentCache(',
    'new CodyCatchmentRebuildCoordinator(_codyL2)',
    'CODY runtime initialized from current runtime'
)) {
    if (!$initialize.Contains($required)) {
        throw "Missing CODY runtime-identity contract: $required"
    }
}

if ($initialize.Contains('knowledge.Data.modSet.fingerprint') -or
    $initialize.Contains('knowledge.Data.valheim.assemblySha256')) {
    throw 'InitializeCody must not key its runtime cache from stale embedded certification identity.'
}

Write-Host "CODY CURRENT-RUNTIME CACHE IDENTITY: PASS" -ForegroundColor Green

Write-Host "`n=== RESULT ===" -ForegroundColor Cyan
Write-Host "MOD-MISMATCH -> STALE KNOWLEDGE DISABLED + CODY ACTIVE: PASS" -ForegroundColor Green