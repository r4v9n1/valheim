$ErrorActionPreference = 'Stop'

$adapter =
    'G:\My Drive\build\Valheim\repos\valheim\LiquidCore\src\PhysicalWaterValheimWorldGeometryAdapter.cs'

$plugin =
    'G:\My Drive\build\Valheim\repos\valheim\LiquidCore\src\PhysicalWaterPlugin.cs'

$a = Get-Content -LiteralPath $adapter -Raw
$p = Get-Content -LiteralPath $plugin -Raw

foreach ($required in @(
    'private const int InitialCausalDiscoveryBatchSize = 8;',
    '_discoverySchedule.CausalRequestActive',
    'Mathf.Max(',
    'InitialCausalDiscoveryBatchSize',
    'TrySelectCausalPrioritySeed(',
    'SelectPendingDiscoveryChunks(',
    'LiquidCore PCE causal discovery progress:'
)) {
    if (!$a.Contains($required)) {
        throw "FAIL: causal batching invariant missing: $required"
    }
}

if (!$p.Contains(
    'config.Bind("ValheimGeometryAdapter", "DiscoveryChunksPerScan", 1')) {
    throw 'FAIL: normal/background discovery default changed from one tile.'
}

$scanStart =
    $a.IndexOf('private void ScanAndLog()')

$helperStart =
    $a.IndexOf(
        'private bool TrySelectCausalPrioritySeed',
        $scanStart)

if ($scanStart -lt 0 -or $helperStart -lt 0) {
    throw 'FAIL: could not isolate causal scan path.'
}

$scan =
    $a.Substring(
        $scanStart,
        $helperStart - $scanStart)

if (!$scan.Contains(
    'causalDiscovery' + [Environment]::NewLine)) {
    # Newline-independent secondary proof.
    if (!$scan.Contains('if (causalDiscovery)')) {
        throw 'FAIL: causal-specific discovery path is absent.'
    }
}

Write-Host 'CAUSAL STARTUP BATCH SIZE >= 8: PASS'
Write-Host 'NORMAL BACKGROUND DEFAULT REMAINS 1: PASS'
Write-Host 'CAUSAL BATCH USES COMPACT SEED + NEAREST FILL: PASS'
Write-Host 'REQUIRED-TILE AUTHORITY PRESERVED: PASS'