# Changelog

## 0.6.8

- Adds observed client/server test notes to the Thunderstore details, including freshly discovered dungeon load timings around 1.5-3.0 ms during the measured test pass.
- Documents that the server remained in ownership maintenance mode without TerramizerServer exceptions while dungeon discovery was tested.
- No runtime behavior changes intended relative to 0.6.7.

## 0.6.7

- Moves the human/AI development disclosure to the top of the Thunderstore details text so it is visible immediately.
- Adds a concise human-directed, AI-assisted development note to the package manifest description.
- No runtime behavior changes intended relative to 0.6.6.

## 0.6.6

- Adds a two-stage broad ZDO scanner: fast warm-up until the first full pass completes, then lower-cost maintenance scanning.
- Persists completed broad-scan pass count in the ownership cache so restarts can return to maintenance mode after warm-start when appropriate.
- Adds maintenance scan config for interval, claim limit, and ZDO records per pass.
- Extends compact ownership summaries with scan mode, pass count, interval, record budget, and claim limit.

## 0.6.5

- Adds an explicit Terramizer companion RPC response so clients can detect the server companion reliably.
- Keeps server-synced player-data metadata as a fallback for older Terramizer clients.
- Stops re-logging broad ZDO scan resume messages during normal in-session scanner progress.
- Changes watched per-object claim logging to off by default; the 30-second ownership summary remains the main view.
- Adds companion RPC reply counts to the compact ownership summary.

## 0.6.4

- Refreshes TerramizerServer companion metadata every 10 seconds instead of advertising only once.
- Persists broad ZDO scan progress in the ownership cache with scan index, object count, and timestamp metadata.
- Resumes the broad eligibility scanner from the cached scan index after restart when the cache is fresh.
- Adds the current `zdoScanIndex` to compact ownership summaries for restart verification.

## 0.6.3

- Advertises TerramizerServer version, static-ownership state, and ownership-cache state through Valheim server-synced player data.
- Allows Terramizer clients to auto-detect the server companion and enable their companion smoothing profile without a custom RPC or required handshake.

## 0.6.2

- Added an ownership cache stored as `r4v9n1.terramizerserver.ownership-cache.tsv` beside the BepInEx config.
- Records known eligible claimed ZDO ids with prefab hash, creator id, prefab name, and timestamp.
- On restart, warm-starts by direct cached ZDOID lookup and applies the new server session id to remembered pieces.
- Adds cache age, cache records per pass, and max restores per pass config.
- Extends the 30-second summary with cache restored, cache checked, cache entries, and cache removed counters.

## 0.6.1

- Changed claim logging defaults so every claimed piece is no longer logged on established worlds.
- Added watched-claim logging for sensitive interactive prefabs such as beds, chests, portals, fires, stations, signs, item stands, wards, doors, and gates.
- Changed the periodic dev summary to a compact 30-second view with interval claims, total claims, watched claims, skipped count, checked ZDO count, and attempts.

## 0.6.0

- Added an experimental static-piece server-ownership module for fresh-world dedicated server testing.
- Enabled the experiment by default and disabled dry-run mode by default for this test build.
- Preserves Valheim's player `creator` field while changing ZDO network ownership to the server for eligible loaded pieces.
- Limits claims to persistent, player-created pieces with `WearNTear`.
- Skips dynamic/physics-heavy objects such as ships, carts, dropped items, creatures, and floating objects.
- Added development claim logs and compact periodic ownership summaries.
- Added direct ZDO database scanning for dedicated-server testing when server-side `Piece` components are not instantiated.
- Keeps `WearNTear`, terrain, world saves, object creation/removal, and active-area handling intact.

## 0.5.1

- Simplified the dedicated-server companion to a non-invasive Unity runtime optimization.
- Disabled Unity's development-only job debugger in batch mode without changing simulation behavior.
- Restored vanilla ZDO synchronization, object handling, active-area checks, ownership, structural support, terrain, sleep, and world-save behavior.
- Removed Harmony and Valheim game-assembly references from the server build.
- Automatically removes obsolete networking, streaming, world-object, ownership, sleep, and diagnostics config entries; the config does not need to be deleted.
- Retained compatibility with vanilla clients and Terramizer clients without a handshake or matching version.
