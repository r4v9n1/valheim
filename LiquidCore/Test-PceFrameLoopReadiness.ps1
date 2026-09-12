$ErrorActionPreference = 'Stop'

$repo = 'G:\My Drive\build\Valheim\repos\valheim'

$pce =
    Join-Path $repo 'LiquidCore\src\LiquidCorePceRuntime.cs'

$runtime =
    Join-Path $repo 'LiquidCore\src\PhysicalWaterDevE1Runtime.cs'

$p = Get-Content -LiteralPath $pce -Raw
$r = Get-Content -LiteralPath $runtime -Raw

# ------------------------------------------------------------
# Frame-loop body must NEVER request the cloned global domain.
# ------------------------------------------------------------

$updateStart =
    $r.IndexOf('private void Update()')

$updateEnd =
    $r.IndexOf(
        'private void TryCommitVanillaWaterPresentationHandoff()',
        $updateStart)

if ($updateStart -lt 0 -or $updateEnd -lt 0) {
    throw 'FAIL: could not isolate PhysicalWaterDevE1Runtime.Update().'
}

$update =
    $r.Substring(
        $updateStart,
        $updateEnd - $updateStart)

if ($update.Contains('TryGetCompleteInitialWorldDomain(')) {
    throw 'FAIL: frame loop still deep-clones the complete 1600-partition PCE domain.'
}

if (!$update.Contains('TryGetCompleteInitialWorldDomainReadiness')) {
    throw 'FAIL: frame loop does not use the allocation-free readiness query.'
}

# ------------------------------------------------------------
# Readiness accessor itself must not clone.
# ------------------------------------------------------------

$readyStart =
    $p.IndexOf(
        'internal bool TryGetCompleteInitialWorldDomainReadiness')

$cloneStart =
    $p.IndexOf(
        'internal bool TryGetCompleteInitialWorldDomain(',
        $readyStart)

if ($readyStart -lt 0 -or $cloneStart -lt 0) {
    throw 'FAIL: readiness/global-domain accessors were not found.'
}

$readyMethod =
    $p.Substring(
        $readyStart,
        $cloneStart - $readyStart)

if ($readyMethod.Contains('.Clone()')) {
    throw 'FAIL: readiness query allocates by cloning the global domain.'
}

# ------------------------------------------------------------
# Clone semantics remain for explicit snapshot consumers.
# ------------------------------------------------------------

$cloneTail =
    $p.Substring(
        $cloneStart,
        [Math]::Min(
            500,
            $p.Length - $cloneStart))

if (!$cloneTail.Contains(
        '_completeInitialWorldDomain?.Clone()')) {
    throw 'FAIL: explicit complete-domain accessor lost isolation semantics.'
}

Write-Host 'FRAME LOOP COMPLETE-DOMAIN CLONES: ZERO PASS'
Write-Host 'READINESS QUERY: ALLOCATION-FREE PASS'
Write-Host 'EXPLICIT SNAPSHOT CLONE SEMANTICS: PRESERVED PASS'