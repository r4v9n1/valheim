# PhysicalWater CHANGELOG

## 0.4.2 - Visual Fidelity

0.4.2 deliberately freezes the successful 0.4.1 Ocean Core Rewrite and concentrates on making that ocean look and feel like water before boat/fish/player physics are revisited.

### Foundation preserved
- The five-level concentric LOD ocean introduced in 0.4.1 is unchanged.
- The six-band deep-water dispersion spectrum and CPU/GPU phase synchronization are unchanged.
- The local physics/ripple field remains separate from the long-range ocean.
- The old finite near/far sheet renderers remain disabled.
- Vanilla Valheim water rendering, flotation, materials and audio remain excluded from the active water stack.
- Boat physics is intentionally not redesigned in this release. The known inverted/unstable boat behavior is deferred until the water/shore visual milestone is complete.

### True depth-aware water optics
- The main camera is now forced to provide Unity's scene depth texture while PhysicalWater is active.
- The water shader measures the eye-space distance between the transparent water surface and opaque terrain/rocks/objects below it.
- Refracted scene colour now uses Beer-Lambert-style per-channel absorption instead of a mostly angle/LOD-driven tint.
- Shallow water preserves more seabed colour and clarity.
- Deep water progressively loses red/green transmission and transitions toward the configured deep-ocean scattering colour.
- Refraction distortion now scales with optical depth and weather roughness.
- Caustic shimmer is depth-limited so it fades out in deep water instead of behaving like decorative full-ocean noise.

### Real environment lighting and reflection response
- Removed the hard-coded fake sun direction.
- The shader now runs as a ForwardBase pass and uses Unity/Valheim's active main directional light and light colour for water glints.
- Fresnel reflection now blends toward Unity's live ambient sky/equator colours so dawn, night, storm skies and biome lighting influence the ocean.
- Rain/storm roughness broadens and weakens direct glints instead of leaving the ocean mirror-polished.

### Valheim fog and weather integration
- Added Unity fog shader variants and applies the active scene fog to the final transparent ocean colour.
- Distant water should now disappear naturally into mist, rain and fog instead of remaining crystal-clear to the LOD horizon.
- PhysicalWater reads the current Valheim environment name to derive a precipitation blend for Rain, LightRain, ThunderStorm, SwampRain, AshRain, snowstorms and mist.
- Precipitation increases fine surface breakup and slightly reduces long-distance reflection contrast.
- Existing wind/storm state continues to control spectrum amplitude and whitecaps.

### Three-scale surface detail
- Added a third high-frequency normal sample for micro chop and precipitation breakup.
- Broad, fine and micro normals travel at different speeds/directions in world space.
- Fine detail fades with LOD distance so the horizon does not shimmer or alias.
- Live ripple gradients remain a separate normal contribution, preserving interaction visibility without moving the ocean mesh.

### GPU shoreline and interaction foam
- Added depth-derived shoreline foam directly in the water shader.
- Foam now appears where the water surface approaches opaque terrain/rocks and is modulated by crest energy and procedural foam/noise.
- Local ripple energy and ripple gradients feed GPU foam, making swimming/object disturbances visible without requiring a particle burst for every contact.
- Crest whitecaps and storm whitecaps remain layered with shoreline and interaction foam.

### CPU effect performance cleanup
- BepInEx diagnostics from 0.4.1 showed approximately 3,000-4,400 shoreline emissions and 700-1,300 interaction particles per diagnostic window.
- Continuous shoreline appearance is now GPU-owned; CPU shoreline particles are sparse accents only.
- Default ShorelineEffectsInterval increased to 0.32 s.
- Default ShorelineEffectsBudget reduced to 28.
- Runtime shoreline budget is hard-capped at 64.
- Interaction particle cadence reduced from 0.08 s to 0.18 s.
- Splash and foam burst counts are capped at 3 and only stronger contacts spawn foam decals.
- This is intended to remove the laggy/stuttering interaction presentation observed in 0.4.1 while improving visual continuity.

### Underwater surface response
- The shader receives whether the active camera is physically below the PhysicalWater surface.
- Underwater views use stronger absorption, reduced direct glint and a different alpha response.
- This is the first optical underwater pass; a dedicated underwater post-process/volume can be added later if the base surface test proves stable.

### Build/version discipline
- Version bumped to 0.4.2 / assembly 0.4.2.0.
- New versioned shader, system, plugin, patches, LOD renderer, project file, bundle builder, build script, installer and BAT.
- Every staged 0.4.2 source/build input remains mandatory.
- Build rejects a DLL whose assembly version is not exactly 0.4.2.0.

### Next milestone
- Validate water colour/depth, fog/rain integration, shoreline foam, ripple visibility, caustics, underwater appearance and frame pacing.
- Continue visual-only tuning until the ocean/shore presentation is considered a stable milestone.
- Only then begin the next phase: boat stability/buoyancy, player/fish/floating-object interaction physics and wake behaviour.
