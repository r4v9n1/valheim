$ErrorActionPreference = 'Stop'

$path =
    'G:\My Drive\build\Valheim\repos\valheim\LiquidCore\src\PhysicalWaterDevE1Runtime.cs'

$text = Get-Content -LiteralPath $path -Raw

$callbackStart =
    $text.IndexOf(
        'private void OnCodyCatchmentPublished')

$callbackEnd =
    $text.IndexOf(
        'private void EvaluateMicroBody',
        $callbackStart)

if ($callbackStart -lt 0 -or $callbackEnd -lt 0) {
    throw 'FAIL: could not isolate CODY publication callback.'
}

$callback =
    $text.Substring(
        $callbackStart,
        $callbackEnd - $callbackStart)

if (!$callback.Contains(
        'TryResumeInitialWorldWaterAfterCodyPublish(descriptor);')) {
    throw 'FAIL: CODY publication does not retry initial-water startup.'
}

$retryIndex =
    $callback.IndexOf(
        'TryResumeInitialWorldWaterAfterCodyPublish(descriptor);')

$mvcEarlyReturnIndex =
    $callback.IndexOf(
        'if (_pendingMvcBodies.Count == 0) return;')

if ($mvcEarlyReturnIndex -lt 0 -or
    $retryIndex -lt 0 -or
    $retryIndex -gt $mvcEarlyReturnIndex) {

    throw 'FAIL: MVC early-return still blocks initial-water retry.'
}

foreach ($required in @(
    'descriptor.Validity != CodyCatchmentValidity.Valid',
    'ContainsBounds(descriptor.DependencyBounds, _domain.WorldBounds)',
    'PublishAppliedCapacityStorage(',
    'TryMaterializeInitialWorldWaterForActiveWindow(',
    'PW_E3_INITIAL_SOURCE_MATERIALIZED trigger=CODY-rebuild'
)) {
    if (!$text.Contains($required)) {
        throw "FAIL: CODY retry invariant missing: $required"
    }
}

foreach ($required in @(
    'persistenceRestoreMs',
    'sourceReplayMs',
    'coverageGateMs'
)) {
    if (!$text.Contains($required)) {
        throw "FAIL: startup timing split missing: $required"
    }
}

Write-Host 'CODY REBUILD -> INITIAL-WATER RETRY: PASS'
Write-Host 'MVC EARLY RETURN NO LONGER BLOCKS STARTUP: PASS'
Write-Host 'VALID CODY COVERAGE REQUIRED: PASS'
Write-Host 'APPLIED STORAGE RETRY: PASS'
Write-Host 'CROSS-RESOLUTION MATERIALIZATION RETRY: PASS'
Write-Host 'PERSISTENCE/SOURCE-REPLAY TIMING SPLIT: PASS'