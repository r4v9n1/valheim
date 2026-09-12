$ErrorActionPreference = 'Stop'

$runtime =
    'G:\My Drive\build\Valheim\repos\valheim\LiquidCore\src\PhysicalWaterDevE1Runtime.cs'

$streaming =
    'G:\My Drive\build\Valheim\workspace\LiquidCore\UnityPhysicalOcean\Runtime\VolumetricStreamingDomain.cs'

$r = Get-Content -LiteralPath $runtime -Raw
$s = Get-Content -LiteralPath $streaming -Raw

$methodStart =
    $r.IndexOf('private void OnCompleteInitialWorldDomainPublished')

$methodEnd =
    $r.IndexOf('private void OnWaterBodyRegistryPublished', $methodStart)

if ($methodStart -lt 0 -or $methodEnd -lt 0) {
    throw 'FAIL: could not isolate complete initial-world publication method.'
}

$commitMethod =
    $r.Substring($methodStart, $methodEnd - $methodStart)

if ($commitMethod.Contains(
    'VanillaWaterSuppression.HideExistingWaterRenderers()')) {

    throw 'FAIL: global source ownership still directly suppresses vanilla water.'
}

foreach ($required in @(
    'private void TryCommitVanillaWaterPresentationHandoff()',
    '_streaming.HasActiveInitialWorldWaterRepresentation',
    '_appliedGeometryStateRevision == int.MinValue',
    '_completedSimulationSteps <= 0',
    '_domain.Surface == null',
    '!_domain.Surface.Ready',
    'VanillaWaterSuppression.HideExistingWaterRenderers();',
    '_vanillaWaterPresentationHandoffComplete = true;'
)) {
    if (!$r.Contains($required)) {
        throw "FAIL: runtime presentation gate missing: $required"
    }
}

foreach ($required in @(
    'private bool _activeInitialWorldWaterRepresentation;',
    'HasActiveInitialWorldWaterRepresentation',
    '_activeInitialWorldWaterRepresentation = true;',
    '_activeInitialWorldWaterRepresentation = false;'
)) {
    if (!$s.Contains($required)) {
        throw "FAIL: active initial-water representation state missing: $required"
    }
}

Write-Host 'GLOBAL SOURCE COMMIT -> VANILLA SUPPRESSION: DISCONNECTED PASS'
Write-Host 'ACTIVE LOCAL WATER GATE: PASS'
Write-Host 'GEOMETRY GATE: PASS'
Write-Host 'COMPLETED STEP GATE: PASS'
Write-Host 'SURFACE READY GATE: PASS'
Write-Host 'FINITE PRESENTATION HANDOFF: PASS'