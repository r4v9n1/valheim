# PhysicalWater CHANGELOG

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
