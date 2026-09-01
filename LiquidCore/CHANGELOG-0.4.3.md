# PhysicalWater CHANGELOG

## 0.4.3 - Visual Fidelity Compatibility Fix

0.4.3 carries the 0.4.2 Visual Fidelity milestone forward unchanged visually and fixes the Valheim 0.221.12 compile break discovered before runtime testing.

### EnvMan compatibility
- Removed direct compile-time access to `EnvMan.m_forceEnv` and `EnvMan.m_currentEnv`; those members are non-public in the current Valheim assembly.
- Added a reflection bridge that resolves forced/current environment state at runtime without binding PhysicalWater to Valheim private fields.
- The bridge accepts field/property variants and several possible environment getter methods so later Valheim revisions are less likely to break compilation.
- If no environment name can be resolved, water weather roughness still falls back safely to live `RenderSettings.fog` and PhysicalWater wind/storm state.
- Reflection failures are diagnostic-only and cannot disable the ocean or shader.

### 0.4.2 visual work preserved
- Five-ring concentric LOD ocean and 0.4.1 spectrum foundation are unchanged.
- Scene-depth Beer-Lambert absorption is unchanged.
- Valheim/Unity scene fog shader variants are unchanged.
- Real directional-light glints and ambient-sky reflection response are unchanged.
- Three-scale normals, depth-sensitive refraction, shallow caustics, crest foam, GPU shoreline foam and live-ripple shading are unchanged.
- CPU shoreline/interaction emission throttling from 0.4.2 is unchanged.
- Boat physics remains deliberately deferred until the ocean/shore visual milestone is complete.

### Build/version discipline
- Version bumped to 0.4.3 / assembly 0.4.3.0.
- New versioned shader, system, plugin, patches, LOD renderer, project file, bundle builder, build script, installer and BAT.
- Every staged 0.4.3 source/build input is mandatory.
- Build rejects a DLL whose assembly version is not exactly 0.4.3.0.

### Next milestone
- Build and visually validate the 0.4.2/0.4.3 depth, fog/rain, lighting, foam, caustics, underwater response and frame pacing in Valheim.
- Continue visual-only tuning until the water/ocean presentation is considered a stable milestone.
- Then begin boat stability/buoyancy and player/fish/floating-object interaction physics.
