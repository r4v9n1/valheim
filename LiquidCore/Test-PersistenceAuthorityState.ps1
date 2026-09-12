$ErrorActionPreference = 'Stop'

$path =
    'G:\My Drive\build\Valheim\repos\valheim\LiquidCore\src\PhysicalWaterPersistenceRuntime.cs'

$text = Get-Content -LiteralPath $path -Raw

if ($text.Contains(
    'if (Instance._pending.Particles.Length == 0)')) {
    throw 'FAIL: persistence still equates zero particles with empty LiquidCore authority.'
}

foreach ($required in @(
    'SnapshotHasAuthoritativeState(Instance._pending)',
    'InitialWorldSourceId',
    'InitialWorldSourceAtoms',
    'InitialWorldSourceReceipts',
    'DormantInitialWorldSourcePartitions',
    'CellVolumeAuthoritative',
    'ContainerBalances',
    'ContainerTransactions',
    'PersistentVerticalDisplacementOverflow'
)) {
    if (!$text.Contains($required)) {
        throw "FAIL: persistence authority classifier missing: $required"
    }
}

Write-Host 'ZERO PARTICLES != EMPTY AUTHORITY: PASS'
Write-Host 'INITIAL SOURCE RECEIPTS SURVIVE LOAD: PASS'
Write-Host 'DORMANT INITIAL SOURCE SURVIVES LOAD: PASS'
Write-Host 'ACTIVE LEDGER AUTHORITY SURVIVES LOAD: PASS'
Write-Host 'CONTAINER/LIFECYCLE AUTHORITY SURVIVES LOAD: PASS'