# LiquidCore

`LiquidCore` is a finite, physically simulated liquid system being developed first for Valheim.

Version `0.6.0-devE3.2` adds conservative logical-region streaming around the
frozen live-passing E1 APIC/FLIP solver and E2.1.2 surface presentation. One
`72 x 18 x 48 m` compact GPU window contains stable `24 x 24 m` world-space
logical regions and one MAC pressure solve across every internal seam. Explicit
finite fluid can cross a former 24 m boundary without a wall, reset, duplicate,
or vanilla-water fallback. Active/dormant ownership preserves stable particle
IDs, volume, velocity, and APIC state; future occupancy/SDF fields are prepared
off-frame with causal geometry revisions before a whole-cell window rebase.

Finite streaming is enabled by default for new installations through the
`[StageE1] Enabled` key. Existing config values remain authoritative. The
finite path is source-gated by complete PCE/CODY state and remains fail-closed
without it; it does not create water from a local window. The focused
diagnostic controls are `pw_e3_*` or F6-F10; the old `pw_e1_*` names remain
aliases.

The preserved devD6.4 adapter adds 8 m fine-tiled live discovery around the preserved
32 m causal SDF queue, targeted event streaming, and state-filtered door updates
on top of the lifecycle, preview, vegetation, bird-fallback, sampled-terrain,
geometry-adapter correctness, and cached
discovery, and live Valheim geometry-event diagnostics on top of the validated `0.6.0-devD5`
streaming adapter. This remains a diagnostics-only bridge from real Valheim
world geometry toward the validated `0.6.0-devD2` dynamic solid SDF pipeline:

- scans a configurable local region around the player/camera, not the whole world
- snaps the local scan domain, tracks active/loaded/evicted chunks, and reports
  cache hits/misses plus frame-budget warnings
- logs live dirty events with dirty reason, source ID/type, old/new AABB, dirty
  voxel bounds, dirty cell count, cache hit/miss state, and split timing fields
- processes dirty geometry through a generation-checked chunk queue so newer
  changes supersede stale queued work before any occupancy/SDF/upload apply
- reports active CPU budget used per frame, queue latency, completion latency,
  pending chunks, stale jobs discarded, chunks coalesced, worst queue depth, and
  frame-budget overruns
- classifies `Heightmap`, `TerrainComp`, `Piece`, `WearNTear`, `Door`,
  `Destructible`, `MineRock`, `MineRock5`, `DungeonGenerator`, `Room`, and
  `Location` hierarchies
- samples nearby `Heightmap` terrain directly, compares sampled heights against
  terrain colliders where available, and estimates occupied terrain columns
- rejects ships, birds, players, creatures, fish, dropped items, floaters, vanilla
  water/liquid volumes, trigger-only colliders, vegetation, and particle/effect
  objects from static SDF ingestion
- rejects unplaced wall/floor build previews until they have a stable Valheim
  network identity, and listens for authoritative network destruction instead
  of ordinary Unity object teardown
- hashes actual Heightmap samples only after a completed dirty regeneration or
  first discovery; unchanged cached terrain retains the cheap revision path
- tracks source IDs, transforms, non-uniform scale, revisions, old/new bounds,
  added/changed/removed objects, and estimated padded dirty voxel regions
- marks the adapter cache dirty after terrain edits, piece placement/destruction,
  heightmap regeneration/pokes, door/gate state changes, and
  destructible/mine-rock updates
- handles known event sources directly without repeatedly rediscovering every
  collider in their dense 32 m chunk; unresolved sources still use bounded
  discovery and periodic consistency scans
- filters duplicate door state notifications and performs one coalesced delayed
  check after animated door colliders settle
- does not enable gameplay water, suppress vanilla water, change buoyancy,
  change swimming, mutate saves, or patch Valheim terrain into live fluid yet

Enable only the adapter scanner with:

```text
[General]
Enabled = false

[ValheimGeometryAdapter]
DiagnosticsEnabled = true
ScanRadius = 64
ScanInterval = 2
EstimatedVoxelCellSize = 0.75
DirtyPaddingCells = 4
LogRejectedSamples = false
DebugVisualization = false
ChunkSize = 32
OriginSnapMeters = 16
TerrainSampleSpacing = 3
TerrainMaxSamplesPerScan = 2048
FrameBudgetMilliseconds = 16
IncrementalBudgetMilliseconds = 4
```

The local installer writes this diagnostics-only shape and explicitly leaves
`General.Enabled=false`, `RenderPreviewSurface=false`, water-query overrides,
vanilla-water suppression, swimming feeds, buoyancy feeds, and interaction hooks
off. Pass `-EnableStageE3` only for the one explicit finite streaming test. If
Valheim is already running when the DLL is installed, restart Valheim before
expecting BepInEx to load the new assembly.

E3 tests 1-13 pass offline, including seam flow, equivalent seam layouts,
terrain/wall/opening behavior, exact dormant restoration, repeated cycles,
three-region/branching bodies, presentation continuity, prepared SDF equality,
and stale geometry preparation rejection. Fresh isolated Unity processes also
pass every frozen Stage A through E2.1.2 regression. Focused inland live E3
certification remains pending; gameplay and global water remain off.

The goal is to replace Valheim's visual/query-only ocean with a controlled runtime water system that can later be used during world generation and map creation.

Version `0.3.11` is the Unity-first anti-jelly checkpoint after testing proved
that `0.3.9` had reintroduced the wrong failure mode: a visually animated mesh
sheet that read like jelly. Vanilla water can still be suppressed, the ocean
floor can still be made walkable, and `0.3.4+` still keeps visible/gameplay water
masks on the same connected-ocean source. The current direction is stricter:
gameplay water stays stable, the visible water comes from the Unity-built
PhysicalOcean material/effects bundle, and water motion must come from shader
normals, foam, caustics, wake/contact layers, audio, and overlays rather than
moving the water plane itself:

- creates a large Unity runtime water mesh around the local player
- treats the world like a sea-level basin fill: terrain below calm sea level is water, terrain above it is land
- requires local ocean connectivity for gameplay water inside the active grid, so isolated dug holes should not automatically fill with ocean water
- uses a ship/floating-probe shoreline fallback so Valheim's five boat probes do not average one dry shore corner into a no-water result
- masks land cells out of the visible water mesh so waves do not decide whether water exists
- prefers the Unity-authored PhysicalOcean material/prefab bundle from `physicalwater_assets` as the primary visual/effects package
- can still adopt a cloned Valheim water material/shader as a fallback if the Unity bundle/material is unavailable
- makes common floating-object water/liquid-height and underwater queries authoritative before vanilla water can decide the result
- uses a 161x161 snapped local water grid; geometry stays cached between origin/mask changes and must not be animated every frame as a sheet
- enables a cheap lowered/feathered far-ocean impostor so the player-following near mesh does not expose a hard square boundary from high altitude
- adds softer generated splash/foam particle textures so effects no longer depend on hard default square particles
- boosts visible shoreline foam shimmer along water/land mask boundaries for the current test pass
- adds a dedicated shoreline foam overlay mesh built from the water/land mask so shore feedback is not dependent only on particle visibility
- loads the Unity-built PhysicalOcean water material and visual-effects prefab from `physicalwater_assets` beside the DLL
- enables PhysicalWater as the character/underwater water source while vanilla water remains suppressed
- enables floating-object/player water feeds while keeping ship physics on Valheim's existing five-point buoyancy system
- bypasses Valheim's vanilla underwater camera clamp so the camera can follow the player underwater
- caches terrain/shoreline masks and visible triangle indices instead of rebuilding them every frame
- emits throttled boat/player/object foam, splash particles, and adopted water audio without deforming gameplay water
- runs a direct local-player contact loop as an extra safety net so wading/swimming produces visible surface foam/ripple feedback even if Valheim character update timing misses a contact tick
- primes ship liquid level before native ship physics, then uses a hull-local float-collider-bottom boat flotation assist in full replacement mode, with stronger capped vertical recovery if the hull keeps sinking below the replacement surface
- corrects the ship target waterline so `m_waterLevelOffset` raises the desired center instead of lowering it or being under-applied
- applies PhysicalWater-side sail, paddle, rudder, and wake support from Valheim's public ship state while vanilla water volumes are suppressed
- applies upright torque, angular damping, and angular-velocity limiting to ships so a tilted hull is stabilized instead of amplified into a sideways launch
- uses the same connected-ocean fallback for generic `Floating.CustomFixedUpdate` liquid feeds that ship probes already use
- explicitly clears character water state when PhysicalWater reports no water, preventing stale swim/wet state from surviving after leaving the replacement water body
- moves player/creature interaction effects toward surface foam/ripple feedback so normal underwater swimming is visible without spawning constant spray above the player
- emits explicit transparent mesh foam decals for player, boat, and shoreline contact so interactions remain visible even if particle rendering is hidden behind water/material ordering
- sinks a wider outer preview boundary below terrain so the prototype edge is harder to see
- disables the old CPU spring heightfield and the later `0.3.9` visual mesh-wave path, because both made the ocean behave like jelly; visual water motion is Unity material/effects-driven instead
- renders a visual sea-level surface while physics/gameplay queries use the same stable sea-level fill
- lets Valheim `Floating.GetLiquidLevel` queries read the prototype surface
- lets ship-style `Floating.GetWaterLevel` and underwater checks read the prototype surface
- can emit splash/foam effects from object interaction without injecting spring displacement into the water mesh
- does not create a collider, so the player should not get trapped in it
- hides vanilla water renderers by default
- suppresses vanilla `WaterVolume.UpdateFloaters()` by default
- can actively feed floating objects from the PhysicalWater surface when physical interaction is enabled
- can actively feed character liquid level from the PhysicalWater surface when physical interaction is enabled
- does not delete vanilla world data or save data
- keeps the proven dry-baseline behavior available through `DryOceanFloorBaseline = true`, where all water queries return dry so the ocean floor is walkable
- in dry baseline, skips vanilla `Floating.CustomFixedUpdate` and `Ship.CustomFixedUpdate`, disables water-volume colliders, and sweeps obvious water/ocean/wave renderers and audio sources
- for the current Unity-first renderer/interaction phase, installs with `DryOceanFloorBaseline = false`, `RenderPreviewSurface = true`, `PhysicalWaterInteractionEnabled = true`, `FeedCharactersLiquidLevel = true`, `FeedFloatingLiquidLevel = true`, `ReactToFloatingObjects = true`, `ShipBuoyancyAssist = true`, `UseVanillaWaterMaterial = false`, `ShorelineEffectsEnabled = true`, `DisableUnderwaterCameraClamp = true`, `RequireOceanConnection = true`, `GridResolution = 161`, `ShorelineVisualBand = 2.25`, `FloatingProbeFallbackRadius = 8.0`, `FarOceanEnabled = true`, `FarOceanRadius = 2200`, `FarOceanInnerRadius = 300`, `WindWaveAmplitude = 0.00`, `MaxSimulatedDisplacement = 0.00`, `ObjectDisturbanceScale = 0.30`, `CharacterDisturbanceScale = 0.42`, `ShorelineEffectsInterval = 0.22`, `ShorelineEffectsBudget = 160`, `ShipBuoyancyAcceleration = 48.0`, `ShipSurfaceLift = 0.15`, `ShipMaxLiftDepth = 4.0`, `ShipVerticalDamping = 18.0`, `ShipWaterDrag = 0.16`, `ShipUprightTorque = 8.0`, `ShipAngularDamping = 1.5`, `ShipMaxAngularVelocity = 1.8`, and `ShipWakeStrength = 0.28`

## Build

```powershell
cd "G:\My Drive\build\Valheim\repos\valheim\LiquidCore"
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The DLL is written to:

```text
%LOCALAPPDATA%\R4V9N1\LiquidCore\dist\LiquidCore.dll
```

## Optional local install

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

This copies the DLL to:

```text
C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\LiquidCore\LiquidCore.dll
```

## Replacement test config

After first launch, BepInEx will create:

```text
BepInEx\config\r4v9n1.physicalwater.cfg
```

The legacy BepInEx GUID and config filename are intentionally preserved so
existing installations retain their settings. The installer removes obsolete
`PhysicalWater.dll` copies before launch to prevent double plugin loading.

Important switches:

- `Enabled = true`
- `DryOceanFloorBaseline = false`
- `RenderPreviewSurface = true`
- `OverrideWaterQueries = true`
- `PhysicalWaterInteractionEnabled = true`
- `FeedFloatingLiquidLevel = true`
- `ReactToFloatingObjects = true`
- `FeedCharactersLiquidLevel = true`
- `DisableUnderwaterCameraClamp = true`
- `ShipBuoyancyAssist = true`
- `HideVanillaWaterRenderers = true`
- `SuppressVanillaWaterVolumeFloaters = true`
- `TerrainMaskEnabled = true`
- `FollowRadius = 384`
- `GridResolution = 161`
- `OriginSnapMeters = 96`
- `VisualBoundarySinkMeters = 96`
- `VisualBoundarySinkDepth = 18`
- `FarOceanEnabled = true`
- `FarOceanRadius = 2200`
- `FarOceanInnerRadius = 300`
- `FarOceanResolution = 65`
- `ShorelineDryMargin = 0.04`
- `RequireOceanConnection = true`
- `ShorelineVisualBand = 2.25`
- `UseVanillaWaterMaterial = false`
- `ShorelineEffectsEnabled = true`
- `ShorelineEffectsInterval = 0.22`
- `ShorelineEffectsBudget = 160`
- `DebugOpaqueWater = false`
- `Damping = 0.90`
- `WindWaveAmplitude = 0.00`
- `MaxSimulatedDisplacement = 0.00`
- `ObjectDisturbanceScale = 0.30`
- `CharacterDisturbanceScale = 0.42`
- `ShipBuoyancyAcceleration = 48.0`
- `ShipSurfaceLift = 0.15`
- `ShipMaxLiftDepth = 4.0`
- `ShipVerticalDamping = 18.0`
- `ShipWaterDrag = 0.16`
- `ShipWakeStrength = 0.28`
- `ShipUprightAssist = true`
- `ShipUprightTorque = 8.0`
- `ShipAngularDamping = 1.5`
- `ShipMaxAngularVelocity = 1.8`
- `FloatingProbeFallbackRadius = 8.0`

Version `0.2.1` is the non-jelly ocean correction after the first Unity visual pass still behaved like the old CPU spring sheet. The AssetBundle contains both the PhysicalOcean material and a Unity-authored visual/effects prefab. The shader/material settings are pushed harder so visual motion is obvious: stronger vertex-wave motion, stronger scrolling normals, moving caustic/noise shimmer, shoreline/ripple coloring, foam response, and glints. BepInEx still owns the Valheim integration: terrain/sea-level fill, vanilla water suppression, water queries, player/creature liquid feeding, and underwater camera behavior.

Important 0.2.1 behavior:

- gameplay water queries return a stable sea level, not the visual wave height
- ships use Valheim's built-in five-point `Ship.CustomFixedUpdate` buoyancy against the replacement water level
- extra `ShipBuoyancyAssist` is disabled by default to avoid stacked lift forces
- the CPU spring heightfield is disabled; object/player interactions emit splash/foam only
- the visible mesh uses procedural/Gerstner-style wave motion and recalculated normals

Version `0.2.2` is the water-body foundation correction after logs showed a split-brain state: the visible surface existed, but player water queries could still report no water (`localPlayerWaterExists=False`, `surfaceY=-10000`). This version intentionally removes all wave/displacement variables from the core test. The visible surface is a flat sea-level renderer, and the gameplay water source returns the same stable sea level for below-sea-level terrain. This is the step that answers "where is the water?": the mesh is only the visible skin; the actual water body for Valheim gameplay is the sea-level query layer underneath it.

Important 0.2.2 behavior:

- gameplay water queries use stable sea level, not visual wave height
- near and far ocean meshes are flat at sea level
- shader vertex wave strength is forced to zero
- CPU heightfield simulation is disabled
- object/player disturbances emit effects only and do not move the mesh
- extra ship buoyancy remains disabled so Valheim's own ship buoyancy is not double-stacked
- local ocean-connectivity flood fill is paused, so isolated dug-hole prevention is not the current test target

Version `0.2.3` keeps the successful non-jelly physics from `0.2.2`, then restores visual water feeling carefully. Far ocean rendering is continuous instead of terrain-patchy, shader motion is slower and directional, shoreline foam is stronger, and boats can emit wake/foam effects. The mesh still does not drive buoyancy. This is intentional: boat physics read the stable water body, while the visual ocean provides believable motion and feedback around that stable body.

Version `0.3.0` is a stronger correction after live testing showed the `0.2.3`
far layer and shader motion still read as square/static/chaotic. The far-ocean
slab is disabled, the local mesh is denser, old CPU spring-water displacement
remains disabled, and the mod now prefers adopting Valheim's own water material
for the replacement mesh. It also adopts water/splash audio clips before
suppressing vanilla water sources, then uses those clips for ambient water and
interaction feedback. This keeps the successful "no vanilla water source of
truth" work while giving the renderer a much better chance of looking like
Valheim water instead of a custom blue dev plane.

Version `0.3.2` keeps the good `Custom/Water` visual result from `0.3.0` but
addresses the two major failures from that test: boats sank, and FPS was around
11. The boat fix is a center-of-mass flotation assist, not the old multi-probe
jelly setup. The performance fix drops the local mesh from 257x257 to 129x129,
stops rewriting mesh vertices every frame, stops recalculating normals every
frame, and spaces out expensive vanilla-water scene sweeps. The interaction
fix adds budgeted shoreline foam particles, stronger movement-based player and
floating-object splash/foam feedback, and diagnostics counters for shoreline
and interaction particle emissions.

Version `0.3.3` is the first "make the patch count" follow-up after testing
showed the water visuals were better but the boat still went to the ocean floor
and the shoreline cut still looked too straight. It changes multiple visible
systems together: the near grid increases to 161x161 without restoring
per-frame mesh rebuilds, shoreline visual overlap widens, shore effects are
slower/softer/cheaper, generated soft particle textures replace default square
billboards, bundled Unity particles are prevented from overriding runtime foam,
and ship flotation now targets the float collider's bottom with a deep-submerge
rescue path. Ship diagnostics now report float-bottom depth as well as center
ride depth.

Version `0.3.4` corrects the water-source split found during inspection. The
visual water mask used ocean connectivity, but gameplay queries still answered
from raw "terrain below sea level" logic. Inside the active grid, gameplay
water now uses the same connected-ocean mask as the visual water. To keep ships
from losing buoyancy when one of Valheim's five probe points lands on a masked
shore cell, `Floating.GetWaterLevel` now uses a ship/floater probe mode that
falls back to connected ocean water within `FloatingProbeFallbackRadius`.
`Ship.CustomFixedUpdate` also primes any ship `Floating` component before
native ship physics runs, and diagnostics include `floatingProbeFallbacks`.

Version `0.3.5` is the ship-stability and interaction-effects correction after
testing showed the shoreline/visual water was improving but boats could still
tilt nearly vertical and launch. The root cause direction was that the custom
rescue assist used `BoxCollider.bounds.min.y`; when a boat tilted, the world
AABB expanded downward, so the assist read the hull as deeply submerged and
added even more lift. `0.3.5` samples the float collider's hull-local bottom
instead, lowers the lift/rescue force, raises vertical damping, adds upright
torque, clamps angular velocity, and logs hull-local bottom depth plus
tilt/angular speed. It also fixes the install config so the old aggressive
`0.3.4` ship values are not written over the safer defaults. Interaction
effects are now throttled and waterline-biased: swimmer movement should make
subtle foam/ripples near the surface, not constant splash particles above the
player, while boat wake points still feed visual foam/splash feedback.

Version `0.3.6` is the follow-up audit build before the next in-game water
test. The latest runtime log before this build was still `0.3.4`, so the
`0.3.5` ship-stability code had not yet been tested in Valheim. The audit found
two integration fixes worth making first: generic `Floating.CustomFixedUpdate`
now uses `GetFloatingProbeSurfaceHeight` with the connected-ocean fallback,
matching ship/floater probe behavior, and character liquid feeding now
explicitly clears water state with `-10000` when PhysicalWater reports no water.
Startup verification confirmed BepInEx loads `PhysicalWater 0.3.6` cleanly with
the intended replacement config and no Harmony patch-install failures.

Research/design rule for future water work:

- Use a stable deterministic water body for shared gameplay queries.
- Layer visible motion on top with mesh waves, shader normals, foam, caustics,
  particles, and audio.
- Keep CPU ripple/heightfield simulation local and optional; do not let it drive
  boats/players until it is proven stable.
- Avoid stacked buoyancy. Valheim ships already query multiple water points, so
  extra off-center force probes can produce pogo-stick torque.
- Build far ocean as a separate cheap visual-only layer later; do not mix it
  with near gameplay water until the near renderer is correct.

Useful references checked for this direction:

- NVIDIA GPU Gems, "Effective Water Simulation from Physical Models":
  separates geometric waves from texture/normal-map wave detail for real-time
  water.
- Unity `Rigidbody.AddForceAtPosition`: off-center forces apply torque; this
  matches the old boat-bounce failure mode when extra buoyancy was stacked.
- Unity HDRP water docs: useful conceptual model for water surfaces,
  simulation bands, deformation/foam, and decals even though Valheim is not
  using HDRP.

The installer writes these replacement-test values into the local config file, because BepInEx keeps old values once a config file exists.

## Current status

This is not yet the final Valheim ocean replacement. It is the first aggressive full-scale experiment:

1. hide the vanilla visual ocean
2. stop vanilla water volumes from feeding floaters
3. route direct liquid, ship water-level, and underwater checks to PhysicalWater
4. let PhysicalWater feed character/object water levels
5. use terrain height and sea level as the global water footprint
6. prove the visible surface and gameplay water source agree
7. then restore shoreline masking/connectivity carefully
8. then restore visual motion through Unity shader/material animation
9. only after that, tune boat, swimmer, weather, wake, and multiplayer behavior
