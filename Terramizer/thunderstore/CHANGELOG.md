# Changelog

## 1.0.3

- Updated the player placement transaction-effect patch for Valheim's current five-parameter `Player.PlacePiece` contract.
- Replaced the package icon with a custom-made, non-AI-generated icon.

## 1.0.1

- Rebuilt and repackaged against the current Valheim 1.0 assemblies after the game update.
- Compatibility maintenance release; no intentional change to Terramizer client gameplay or performance behavior.
- Replaced the package icon with a custom-made, non-AI-generated icon.

## 1.0.0

- Rebuilt against the current Valheim 1.0 assemblies.
- Preserves vanilla vegetation, WearNTear, fire, ambient-smoke, visible-update, and multiplayer cadence while retaining the scoped placement-smoke cleanup and allocation safeguards.

## 0.9.8

- Enables Unity collision-callback reuse by default to reduce physics-heavy scene GC without changing physics frequency or visible quality; the setting is opt-out for compatibility.
- Adds allocation-free `BinarySearchDictionary.SetValue` handling for hot value-type update paths, reducing avoidable GC work without reducing visible update ticks or smoke/vegetation quality.
- Caches the active ZDO integer table during `VisEquipment` updates, removing repeated dictionary-root lookups while preserving equipment visuals and update cadence.
- Writes nested `ZPackage` data directly from its existing buffer, removing an avoidable serialization copy while preserving the exact length-prefixed wire format.
- Prefer the server's richer synced terrain metadata over the legacy ownership-only RPC during multiplayer handshake, so compatible 0.6.9 dedicated servers reliably expose their configured 16 m terrain limits even if RPC delivery is delayed.
- Extend the final `TerrainComp.ApplyToHeightmap` clamp as well as the operation clamps, and require a validated TerramizerServer handshake before enabling extended terrain on remote dedicated worlds.
- Bound companion discovery traffic to one legacy probe and three V2 retries per connection instead of repeating both RPCs indefinitely.
- Keeps WearNTear and smoke renderer updates at vanilla cadence, including when a TerramizerServer companion is detected.
- Removes the client smoke-renderer Harmony hook entirely.
- Suppresses only the one-shot placement effect emitted when building pieces are placed; ambient and gameplay smoke remain unchanged.
- Keeps vegetation resets immediate; no grass rebuild deferral or vegetation pacing is enabled.
- Removes obsolete 1.0 pacing and terrain config entries on load so restored clients cannot retain stale throttling settings.
- Adds narrow 16 m terrain raise/dig compatibility for single-player and local hosting; remote servers remain authoritative.
- Adds mixed-version-safe server terrain capability negotiation so compatible remote clients honor the dedicated server's configured limits.

## 0.9.5

- Retunes smoke pacing for smoother visible fire, hearth, smelter, and processing-station smoke.
- Lowers the default smoke update interval from 0.05s to 0.005s.
- Lowers the TerramizerServer companion smoke interval from 0.08s to 0.005s.
- Changes smoke pacing from one global throttle to adaptive per-renderer staggering, so smoke does not freeze and resume in one visible batch.
- Prioritizes smoother-than-vanilla smoke presentation over smoke CPU savings; the main performance gains remain structure pacing and server ownership.
- Keeps the stronger server-companion structure pacing unchanged.

## 0.9.4

- Moves the human/AI development disclosure to the top of the Thunderstore details text so it is visible immediately.
- Documents AI-assisted code analysis and verification while retaining project-directed implementation and release decisions.
- No gameplay changes intended relative to 0.9.3.

## 0.9.3

- Adds an explicit TerramizerServer companion RPC handshake for more reliable auto-detection.
- Keeps server-synced metadata detection as a fallback for older companion builds.
- Adds clearer waiting diagnostics that distinguish missing RPC response from missing synced metadata.

## 0.9.2

- Makes TerramizerServer companion detection more tolerant while server metadata is arriving.
- Enables companion mode when the TerramizerServer version key is present, even if the static-ownership key is delayed.
- Adds compact companion-detection diagnostics while waiting for server metadata.

## 0.9.1

- Adds TerramizerServer companion auto-detection through Valheim server-synced metadata.
- Adds optional server-companion smoothing config for servers using static-piece server ownership.
- Uses companion-specific WearNTear and smoke pacing intervals when TerramizerServer static ownership is detected.
- Extends diagnostic summaries with companion-mode state and detected server version.

## 0.9.0

- Improves frame-time consistency in dense bases by smoothing structure and smoke update work.
- Reduces grass-rebuild spikes by combining repeated reset requests and allowing terrain changes to settle briefly.
- Preserves Valheim's visual quality and normal object-loading behavior.
- Works standalone, with vanilla servers, and with the optional TerramizerServer companion.
