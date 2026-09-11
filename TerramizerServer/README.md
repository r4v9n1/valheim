# TerramizerServer

> [!IMPORTANT]
> ## CLEAN CONFIG REQUIRED FOR THIS VERSION
>
> TerramizerServer 1.0.2 requires a **fresh configuration file**.
>
> Before installing this version:
>
> 1. Stop the Valheim dedicated server.
> 2. Delete the existing `BepInEx/config/r4v9n1.terramizerserver.cfg`.
> 3. Replace the old TerramizerServer DLL with the new version.
> 4. Start the server and allow TerramizerServer to generate a new configuration file.
>
> **Do not reuse the configuration file from the previous release.**
>
> The previous TerramizerServer release contained Valheim 1.0 compatibility bugs and **should not be used**. This release supersedes that version.

TerramizerServer `1.0.2` is a dedicated-server performance companion for Valheim. It keeps the Unity job-debugger optimization, reduces avoidable hot-path allocations, adds bounded area-entry ZDO prefetch with socket backpressure, and includes a server-authority experiment for loaded static player-built structure pieces.

In this test build the static-piece ownership experiment is enabled by default and dry-run mode is disabled. The server claims loaded, persistent, player-created pieces that have `WearNTear`, while preserving Valheim's `creator` field. Dynamic physics objects such as ships, carts, dropped items, creatures, and floating objects are skipped. `WearNTear` is not removed.

The default runtime path is event-driven: newly loaded eligible pieces are handled immediately, while the legacy whole-world ownership audit remains disabled unless explicitly enabled. This keeps established multiplayer worlds responsive. The server also preserves fast sleep time-skipping, skips only the extra sleep-triggered world save, and preserves the regular autosave timer.

## Build and package

```powershell
.\build.ps1
.\build-package.ps1
```

Outputs:

```text
dist\TerramizerServer.dll
G:\My Drive\build\Valheim\releases\TerramizerServer\R4V9N1-TerramizerServer-1.0.2.zip
```

## Install

Replace the older DLL in the dedicated server's `BepInEx\plugins` folder. The config remains:

```text
BepInEx\config\r4v9n1.terramizerserver.cfg
```

Key experiment config:

```text
[ExperimentalOwnership]
EnableStaticPieceServerOwnership = true
DryRunStaticPieceServerOwnership = false
PlayerBuiltPiecesOnly = true
RequireWearNTear = true
OwnershipScanIntervalSeconds = 5
EnableBackgroundOwnershipAudit = true
MaxClaimsPerScan = 250
ZdoRecordsPerScan = 25000
MaintenanceOwnershipScanIntervalSeconds = 30
MaintenanceMaxClaimsPerScan = 100
MaintenanceZdoRecordsPerScan = 5000

[OwnershipCache]
EnableOwnershipCache = true
OwnershipCacheMaxAgeHours = 168
OwnershipCacheRecordsPerScan = 200000
MaxCacheRestoresPerScan = 5000
ResumeZdoScanFromCache = true

[DevLogs]
LogOwnershipClaims = false
LogWatchedOwnershipClaims = false
LogOwnershipSkips = false
OwnershipSummaryIntervalSeconds = 30
```

`LogWatchedOwnershipClaims` can be enabled for focused testing of sensitive interactive prefabs such as beds, chests, portals, fires, crafting stations, signs, item stands, wards, doors, and gates. It is off by default for large-world testing. The periodic summary logs compact claim totals every 30 seconds.

The background ownership audit and ownership cache are enabled by default. The cache is stored beside the BepInEx config as `r4v9n1.terramizerserver.ownership-cache.tsv`. It preserves known eligible claimed ZDO ids across restarts, then warm-starts by directly looking up those ZDO ids and assigning the current server session id. It also persists the broad ZDO scan cursor and completed pass count so large-world scans can resume near the last flushed position after restart and return to lower-cost maintenance scanning once the first full pass is done.

TerramizerServer answers a lightweight companion RPC so Terramizer clients can auto-enable their server-companion smoothing profile reliably. It still refreshes Valheim server-synced metadata as a fallback for older Terramizer clients.

The update automatically removes retired networking, object streaming, ownership, sleep, and diagnostics settings. You do not need to delete the config.

Created by R4V9N1.
