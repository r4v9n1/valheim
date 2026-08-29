# Terramizer

Terramizer `0.9.5` is a standalone client performance mod for Valheim. It improves frame-time consistency in dense bases while preserving the game's visual quality and normal draw distance.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

## Performance features

- Coalesces bursts of repeated grass-reset requests into one equivalent reset.
- Briefly separates full grass rebuilding from terrain-regeneration spikes.
- Paces the client WearNTear updater in structure-heavy areas.
- Uses adaptive client smoke-renderer pacing around fires and processing stations, tuned for smoother visible smoke.
- Disables Unity's development-only job debugger without changing worker counts.
- Provides optional compact FPS and smoothing diagnostics.
- Auto-detects TerramizerServer `0.6.5` or newer through a lightweight companion RPC and enables a stronger companion smoothing profile.

These optimizations reduce repeated or overlapping client work while keeping gameplay calculations consistent.

## Standalone and server compatibility

Terramizer works in:

- Single-player.
- Local hosting.
- Multiplayer on a vanilla server.
- Multiplayer with the optional TerramizerServer companion.

TerramizerServer is not required. Terramizer uses a small optional companion RPC only to detect compatible TerramizerServer installs; there is no required matching-version lock and vanilla servers still work normally. When TerramizerServer reports static-piece server ownership, Terramizer automatically increases client WearNTear pacing through its server-companion profile while prioritizing smoother-than-vanilla smoke presentation. Smoke pacing runs every frame at normal frame rates, and only staggers smoke renderers when there is extreme frame rate headroom. Older server-synced metadata detection remains as a fallback.

## Quality and compatibility

- Preserves texture resolution, render scale, geometry detail, vegetation distance, lighting, and shadows.
- Uses Valheim's normal world-object loading cadence for consistent object appearance.
- Maintains native terrain, structure, networking, ownership, and save behavior.
- Applies focused client update smoothing to grass, structure wear, and smoke rendering.

Config file:

```text
BepInEx/config/r4v9n1.terramizer.cfg
```

Keep your existing config; Terramizer adds its performance settings with tested defaults.

Server companion config:

```text
[ServerCompanion]
EnableServerCompanionOptimizations = true
AutoDetectTerramizerServer = true
ForceServerCompanionMode = false
LogServerCompanionDetection = true
ServerCompanionWearNTearMinIntervalSeconds = 0.16
ServerCompanionSmokeMinIntervalSeconds = 0.005
```
