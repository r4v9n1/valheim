# TerramizerServer

> I use AI to review code and assist with optimization and integrity; I manually test and review every mod, and all decisions and code remain my own.

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

Current release: **1.0.1**, rebuilt against the current Valheim 1.0 dedicated-server assemblies.

TerramizerServer `0.6.9` is a dedicated-server performance companion for Valheim.

It keeps the Unity job-debugger optimization, reuses Unity collision callback objects to reduce physics GC, removes avoidable boxing allocations from Valheim's hot `BinarySearchDictionary.SetValue` update paths, restores bounded server zone-entry prefetch for faster area and dungeon object arrival, and lets the dedicated server claim loaded static player-built structure pieces while preserving Valheim's `creator` field. `WearNTear` is not removed, and dynamic physics objects are skipped.

## Development note

AI is used to assist with code analysis, optimization review, implementation refinement, and verification. I direct the build process, technical decisions, final code, packaging, and in-game validation.

Created and maintained by **R4V9N1**.

## Version 0.6.9

- Reuses Unity collision callback objects by default to reduce dedicated-server physics GC without lowering simulation frequency; disable `ReuseCollisionCallbacks` for mods that retain `Collision` objects after callbacks.
- Preserves fast sleep and skips only the extra sleep-triggered world save.
- Claims eligible static pieces when their ZDO views are created, without scanning the whole live ZDO table during normal operation.
- Prefilters ZDO views by cached prefab eligibility before walking component trees, reducing dedicated-server zone-load work for non-structure objects.
- Optimizes the periodic ownership handoff scan by reusing ZDO's local-owner flag while preserving vanilla active-area decisions.
- Keeps the legacy whole-world ownership audit opt-in (`EnableBackgroundOwnershipAudit = false`).
- Extends both terrain raise and dig limits to 16 m by default.
- Advertises terrain capability and limits through additive metadata and a versioned companion RPC; the legacy RPC remains unchanged for mixed-version clients.
- Removes avoidable boxing allocations from hot server-side `BinarySearchDictionary.SetValue` update paths without changing gameplay simulation or network authority.
- Caches the active ZDO integer table during equipment-visual updates, reducing repeated server-side lookup work without changing equipment state or synchronization.
- Writes nested network packages directly from their existing buffers, reducing server serialization allocations without changing the wire format.
- Restores bounded zone-entry ZDO prefetch to reduce the delay before newly entered areas and dungeon objects arrive, with socket backpressure and no server-side peer-zone scene creation.

## Version 0.6.7

- Adds the experimental static piece server-ownership module.
- Enables the experiment by default for test-server use.
- Preserves player creator metadata separately from ZDO network ownership.
- Skips dynamic/physics-heavy objects such as carts, ships, dropped items, creatures, and floating objects.
- Logs every ownership claim only when explicitly enabled.
- Keeps per-object watched ownership claim logs off by default for large-world testing; they can still be enabled for focused debugging.
- Adds a compact periodic ownership summary with 60-second claim totals when the optional broad audit is enabled.
- Stores a restart ownership cache for known eligible claimed ZDOs.
- Warm-starts restarts by direct cached ZDOID lookup, then assigns the current server session id without repeating the slow full eligibility scan for already known pieces.
- Switches from fast warm-up scanning to lower-cost maintenance scanning after the first full broad ZDO pass.
- Answers a lightweight companion RPC for reliable Terramizer client auto-detection.
- Refreshes Valheim server-synced player-data metadata as a fallback for older Terramizer clients.
- Persists the broad ZDO scan cursor in the ownership cache so large-world scanning can resume after restart.
- Adds direct server ZDO database scanning so dedicated-server records can be tested even when Unity `Piece` objects are not instantiated server-side.
- Keeps `WearNTear` intact.
- Accepts vanilla clients, Terramizer clients, or a mixture of both.
- Requires no matching client version. Vanilla clients can connect normally; the optional companion RPC is only used by Terramizer clients for auto-detection.

Install this package on the **dedicated server only**. The config file is:

```text
BepInEx/config/r4v9n1.terramizerserver.cfg
```

You do not need to delete the config when updating.
On first successful load, retired streaming, throttling, ownership-repair, and sleep-work settings are removed; current streaming, ownership, terrain, and save-policy settings are preserved.

Important config:

```text
[ExperimentalOwnership]
EnableStaticPieceServerOwnership = true
DryRunStaticPieceServerOwnership = false
OwnershipScanIntervalSeconds = 15
MaxClaimsPerScan = 100
ZdoRecordsPerScan = 10000
EnableBackgroundOwnershipAudit = false
MaintenanceOwnershipScanIntervalSeconds = 1800
MaintenanceMaxClaimsPerScan = 25
MaintenanceZdoRecordsPerScan = 1000
PlayerBuiltPiecesOnly = true
RequireWearNTear = true

[Terrain]
EnableExtendedTerrainLimits = true
TerrainRaiseLimitMeters = 16
TerrainDigLimitMeters = 16

[Streaming]
EnableServerZoneStreamingBoost = true
MaxZoneStreamingBoostZdosPerPeer = 256
ZoneStreamingBoostCooldownSeconds = 0.75
ZoneStreamingBoostMaxQueuePercent = 35

[OwnershipCache]
# Used only when EnableBackgroundOwnershipAudit is enabled.
EnableOwnershipCache = true
OwnershipCacheMaxAgeHours = 168
OwnershipCacheRecordsPerScan = 200000
MaxCacheRestoresPerScan = 5000
ResumeZdoScanFromCache = true

[DevLogs]
LogOwnershipClaims = false
LogWatchedOwnershipClaims = false
LogOwnershipSkips = false
```
