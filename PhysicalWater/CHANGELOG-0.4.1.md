# PhysicalWater CHANGELOG

## 0.4.1 - Pre-build Ocean Core Audit Fixes

This patch is the mandatory static-audit pass over 0.4.0 before the first Ocean Core Rewrite build.

### CPU/GPU wave agreement
- GPU Gerstner phase now samples the original undisplaced world XZ for every band, matching CPU phase evaluation.
- Wind interpolation is saturated to the same 0..1 behavior used by Unity `Mathf.Lerp` on CPU.
- Storm direction blending now uses the same 0.22 factor on CPU and GPU.
- Final geometric vertical displacement is bounded to the same +/-1.10 m procedural ceiling used by gameplay sampling.
- CPU queries perform a bounded two-iteration inverse-Gerstner solve so height/velocity are sampled at the visible displaced world XZ rather than the undisplaced parameter coordinate.
- The 3.5 m band was removed from geometric/gameplay height and retained as micro-normal detail; the near 1.5 m mesh cannot represent a 3.5 m geometry wave cleanly.

### LOD anti-alias / anti-jelly hardening
- Each geometric wave band now fades out before its LOD cell spacing becomes too coarse to represent that wavelength.
- LOD fade is continuous by radial position across ring boundaries, so adjacent rings calculate identical displacement at shared seams instead of tearing apart.
- 110 m swell survives to the outer ring; 58/29/14/7 m bands progressively disappear with distance.
- This prevents under-sampled far triangles from recreating cloth-like motion.

### Water shading correction
- Procedural runtime normal textures are decoded directly from RG instead of Unity imported-normal packing, because these textures are generated at runtime rather than imported as NormalMap assets.
- Detail normals are now constructed in Y-up world orientation before blending with the geometric ocean normal.
- Smoothness now controls specular/glint sharpness.
- Removed a bundle-builder write to the nonexistent `_Metallic` property.

### Build/version discipline
- Version bumped to 0.4.1 / assembly 0.4.1.0.
- 0.4.0 should not be built; 0.4.1 supersedes it before first runtime test.

## 0.4.0 - Ocean Core Rewrite

0.4.0 is a deliberate architecture reset of the renderer and ocean model while preserving the hard rule that vanilla Valheim water never returns as an active rendering or physics fallback.

### Breakthrough: concentric ocean LOD renderer
- Added `PhysicalWaterLodOcean`, a camera/player-centred five-level concentric ocean renderer.
- The visible ocean is no longer rendered by the old finite 161x161 near-water sheet or the old far-ocean impostor.
- LOD half-sizes: 96 m, 192 m, 384 m, 768 m, 1536 m.
- LOD cell sizes: 1.5 m, 3 m, 6 m, 12 m, 24 m.
- Ring boundaries are powers of two so adjacent levels share stable seams.
- Short geometric waves fade with distance instead of aliasing across coarse far geometry.
- Old near/far meshes remain only as physics/terrain/shore-mask data and are forced invisible.

### Deep-water spectrum shared by CPU and GPU
- Replaced the old arbitrary four-band time speeds with six deterministic spectral bands spanning 110 m to 3.5 m wavelengths.
- Both CPU gameplay sampling and the GPU shader use deep-water dispersion: `omega = sqrt(g*k)`.
- CPU surface height, normal and water velocity therefore advance from the same physical clock as visible ocean geometry.
- Gerstner horizontal displacement is included in the GPU surface, avoiding the vertical-only fabric-sheet silhouette.
- Local interaction ripples are deliberately NOT added to vertex height in 0.4.0; they affect normals/foam while the separate local physics field drives interaction forces.

### Local interaction simulation separated from long-range ocean rendering
- Local physics/interaction field radius reduced from 384 m to 120 m.
- With the existing 161-point grid this improves local cell spacing from about 4.8 m to 1.5 m.
- Local ripple displacement ceiling reduced to 0.16 m.
- Player/object disturbance energy and ship wake energy reduced accordingly.
- Long-range visible ocean is now independent of this local simulation window.

### Water shader rewrite
- Added multi-scale Gerstner geometry on the LOD ocean.
- Added two world-space scrolling normal layers.
- Added live ripple-gradient normal perturbation and interaction foam.
- Added crest-driven whitecaps and storm whitecaps.
- Added Fresnel response, sun glint and animated caustic/noise detail.
- Added a named GrabPass refraction path so the surface refracts the rendered world instead of reading as a flat tinted decal.
- Shader remains D3D11-only and release build continues to reject unsupported custom shaders.

### Boat physics rewrite
- Replaced the six-probe layout with a 3x3 hydrostatic hull footprint.
- Buoyancy uses a smooth hydrostatic submersion curve and is bounded near gravity instead of using rescue impulses or velocity-change boosts.
- Added a dedicated righting moment toward the averaged PhysicalWater surface normal.
- Inverted boats get a deterministic recovery axis instead of remaining upside down and bouncing on the seabed.
- Vertical velocity, horizontal speed and angular velocity remain bounded.
- No vanilla Ship water solver or WaterVolume flotation is restored.

### No vanilla water
- Vanilla WaterVolume renderers remain suppressed.
- Vanilla LiquidSurface water renderers remain suppressed.
- Vanilla WaterVolume floater updates remain suppressed.
- Vanilla water triggers remain suppressed for water.
- Vanilla ship water physics remains suppressed.
- PhysicalWater does not adopt a Valheim water material or water audio.
- Rendering failures remain PhysicalWater failures; they never silently revive vanilla water.

### Version/build discipline
- Version bumped to 0.4.0 / assembly 0.4.0.0.
- New versioned shader, system, patches, plugin, project file, LOD renderer, bundle builder, build script, installer and BAT.
- Build rejects any DLL whose assembly version is not exactly 0.4.0.0.
