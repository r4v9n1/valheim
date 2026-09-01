# PhysicalWater Changelog

## 0.3.15 - Ship physics + visible water motion

### Fixed
- Removed the legacy single-point custom ship lift/rescue/velocity controller from the active ship path. PhysicalWater now feeds the liquid level and lets Valheim's native Ship/Floating rigidbody logic own movement until proper multipoint wave buoyancy is implemented. This targets boats being pinned half-submerged and then launched by collision contact.
- Disabled the legacy upright torque assist by default for the same reason.
- Near-water triangles now require three real gameplay-water vertices. The visual shoreline band can no longer bridge a large water sheet across dry terrain.
- Boat/player contact effects are no longer discarded merely because the source transform is below the water surface.

### Visuals
- Enabled `_VisualWaveStrength` at 0.68 in both the runtime material setup and Unity AssetBundle material builder.
- Added deterministic wind-driven procedural optical wave bands in the D3D11 fragment shader. Vertices remain static, preserving the no-jelly guardrail.
- Added animated crest foam contribution and stronger moving surface normals.
- Increased splash/foam emission cadence and visibility for player/boat interaction.

### Architecture
- Gameplay water remains a stable deterministic surface for this patch.
- Visual wave motion is shader-side only.
- Proper multipoint boat buoyancy sampling against the future deterministic wave field is the next physics stage.

Versioning rule: every code or behavior patch increments the mod version and adds an entry here before the patched build is considered testable. Build scripts must verify the produced DLL assembly version.

## 0.3.14 - 2026-08-25

### Fixed
- Added the missing `TEXCOORD1` semantic to the world-position interpolator passed from the vertex shader to the fragment shader.
- Made vertex color semantics explicit as `COLOR0`.
- Restricted the Physical Ocean GPU program to D3D11 with `#pragma only_renderers d3d11`, matching the Windows/Valheim target.
- Changed the custom shader to `FallBack Off` so Unity cannot hide a dead Physical Ocean pass behind a supported legacy fallback.
- Added a hard `shader.isSupported` check to the Unity AssetBundle builder; unsupported custom water shaders now abort the build instead of entering `physicalwater_assets`.

### Build / workflow
- Bumped plugin and assembly version to `0.3.14` / `0.3.14.0`.
- Added dedicated 0.3.14 source snapshots, Unity builder, installer, build script, and BAT entry point.
- 0.3.14 is considered shader-valid only when the Unity build log reports one or more D3D11 internal programs for `R4V9N1/Physical Ocean Surface`.

### Next validation
- Rebuild the Unity bundle and verify the prior `All subshaders removed` warning is gone and `d3d11 (total internal programs: >0)` is present before launching Valheim.

## 0.3.13 - 2026-08-25

### Fixed
- Corrected the Unity ocean shader structure so the shader parses instead of failing at the closing SubShader brace.
- Moved Unity AssetBundle builds to a local NTFS cache while keeping `G:\My Drive\dev\water` as the canonical project source, avoiding Unity AssetDatabase/LMDB crashes on the Google Drive virtual filesystem.
- Forced full `physicalwater_assets` regeneration with `BuildAssetBundleOptions.ForceRebuildAssetBundle`, preventing Unity from emitting only the small `dist` manifest after the previous content bundle was deleted.
- Added replacement-renderer readiness gating so vanilla water is not hidden unless PhysicalWater has a usable replacement material and renderable water geometry.
- Added bundled-shader runtime validation/fallback handling so a failed Unity material cannot leave the world with no visible water.
- Corrected ship buoyancy assist to use float-collider hull-bottom immersion rather than forcing the rigidbody center 0.65-2.20 m above sea level.
- Owner-gated small-creature swim physics mutation for multiplayer while retaining client-side liquid-level state feeds.

### Build / workflow
- Added per-patch version bump enforcement.
- Added this changelog as a required companion to every patch.
- Build now expects assembly version `0.3.13.0`.
- Unity bundle remains part of the standard build/install pipeline.

### Known issue / next work
- Unity currently reports `R4V9N1/Physical Ocean Surface` as unsupported for D3D11 with zero internal shader programs in the bundle. The next patch must make the custom Unity shader genuinely Windows/D3D11-compatible before visual gameplay testing.

## 0.3.12 - 2026-08-25

### Baseline
- Maintained the anti-jelly guardrail: CPU heightfield simulation and moving near/far mesh vertex animation remain disabled in the Valheim adapter.
- Continued stable gameplay sea-level queries, connected-ocean masking, shoreline effects, interaction effects, ship hooks, character liquid feeds, and Unity AssetBundle visual-material integration.
- Added small-creature swim assist and multiplayer ownership correction work that was finalized into 0.3.13.
