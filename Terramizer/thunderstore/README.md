# Terramizer

> I use AI to review code and assist with optimization and integrity; I manually test and review every mod, and all decisions and code remain my own.

Current release: **1.0.1**, rebuilt against the current Valheim 1.0 assemblies.

Terramizer `0.9.8` is a standalone client performance mod for Valheim. It applies narrowly scoped client-side safeguards while preserving the game's visual quality and normal draw distance.

## Development note

AI is used to assist with code analysis, optimization review, implementation refinement, and verification. I direct the build process, technical decisions, final code, packaging, and in-game validation.

Created and maintained by **R4V9N1**.

## Performance features

- Keeps vegetation updates immediate, without reducing grass density, draw distance, or visual quality.
- Leaves vanilla WearNTear updates unchanged.
- Leaves fire, ambient, and gameplay smoke behavior unchanged.
- Suppresses only the one-shot smoke burst attached to a newly placed building piece; persistent fire and ambient smoke remain vanilla.
- Disables Unity's development-only job debugger without changing worker counts.
- Reuses Unity collision callback objects to reduce physics GC allocations without reducing physics frequency or visual quality; disable `ReuseCollisionCallbacks` for mods that retain `Collision` objects after callbacks.
- Removes avoidable boxing allocations from Valheim's hot `BinarySearchDictionary.SetValue` update paths without changing update cadence or gameplay.
- Caches the active player's ZDO integer table during equipment-visual updates, avoiding repeated table lookups while preserving all equipment, armor, and player update behavior.
- Writes nested network packages directly from their existing buffers, removing an avoidable serialization copy without changing the wire format.
- Provides optional compact FPS and smoothing diagnostics.
- Auto-detects TerramizerServer through a lightweight, mixed-version-safe companion handshake; `0.6.9` sessions can negotiate the server's terrain limits without changing visible update cadence.

These optimizations reduce repeated or overlapping client work while keeping gameplay calculations consistent.

## Standalone and server compatibility

Terramizer works in:

- Single-player.
- Local hosting.
- Multiplayer on a vanilla server.
- Multiplayer with the optional TerramizerServer companion.

TerramizerServer is not required. Terramizer uses a small optional companion RPC only to detect compatible TerramizerServer installs; there is no required matching-version lock and vanilla servers still work normally. Companion detection never reduces WearNTear or smoke update cadence. Older server-synced metadata detection remains as a fallback, and the legacy companion RPC remains available for mixed-version sessions.

## Quality and compatibility

- Preserves texture resolution, render scale, geometry detail, vegetation distance, lighting, and shadows.
- Uses Valheim's normal world-object loading cadence for consistent object appearance.
- Maintains server-authoritative terrain, structure, networking, ownership, and save behavior; local hosting and compatible TerramizerServer sessions use 16 m terrain raise/dig limits by default.
- Leaves vegetation, structure wear, and smoke updates at immediate vanilla cadence; only placement-burst effects are suppressed.

Config file:

```text
BepInEx/config/r4v9n1.terramizer.cfg
```

Keep your existing config; Terramizer adds its performance settings with tested defaults.
On first successful load, obsolete 1.0 pacing, terrain, and frame-pressure entries are removed; current settings such as `Enabled`, diagnostics, and companion detection are preserved.

Server companion config:

```text
[ServerCompanion]
EnableServerCompanionOptimizations = true
AutoDetectTerramizerServer = true
ForceServerCompanionMode = false
LogServerCompanionDetection = true
```
