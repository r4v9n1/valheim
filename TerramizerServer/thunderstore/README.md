# TerramizerServer

TerramizerServer `0.6.8` is an experimental dedicated-server performance companion for Valheim.

It keeps the Unity job-debugger optimization and adds a fresh-world test feature that lets the server claim loaded static player-built structure pieces while preserving Valheim's `creator` field. `WearNTear` is not removed, and dynamic physics objects are skipped.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

## Observed test results

During R4V9N1 fresh-world dedicated-server testing on 2026-08-22, the ownership cache and maintenance scan path produced the following measured behavior:

- Newly discovered dungeon entries were observed loading on the client in roughly 1.5-3.0 ms across repeated tests.
- Sample client timings included 41 rooms in 1.5241 ms, 35 rooms in 1.5031 ms, 36 rooms in 1.7994 ms, and 33 rooms in 2.0053 ms.
- The slowest observed freshly tested dungeon load in that pass was still only about 3.0071 ms for 33 rooms.
- The server stayed in maintenance mode during dungeon discovery, using a 30-second interval, 5,000 ZDO record budget, and 100 claim limit.
- Server summaries remained stable while the client moved through newly discovered locations, with no TerramizerServer exceptions seen in the test logs.
- Client shutdown was clean, and the server continued ownership maintenance afterward with companion replies dropping to 0 once no Terramizer client was connected.
- The intended benefit is lower ZDO ownership churn and less client-side contention during active world/location streaming, especially after the first broad scan and cache warm-start have completed.

These numbers are real measurements from the test setup, not a universal guarantee. Results will vary with world size, mod list, hardware, loaded areas, and server configuration.

## Version 0.6.8

- Adds measured test-result notes to the Thunderstore details so server owners can understand the observed dungeon-load and maintenance-scan benefits.
- No runtime behavior changes intended relative to 0.6.7.

## Version 0.6.7

- Adds the experimental static piece server-ownership module.
- Enables the experiment by default for test-server use.
- Preserves player creator metadata separately from ZDO network ownership.
- Skips dynamic/physics-heavy objects such as carts, ships, dropped items, creatures, and floating objects.
- Logs every ownership claim only when explicitly enabled.
- Keeps per-object watched ownership claim logs off by default for large-world testing; they can still be enabled for focused debugging.
- Adds a compact periodic ownership summary with 30-second claim totals.
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

Important config:

```text
[ExperimentalOwnership]
EnableStaticPieceServerOwnership = true
DryRunStaticPieceServerOwnership = false
OwnershipScanIntervalSeconds = 5
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
```
