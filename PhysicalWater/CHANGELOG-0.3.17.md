# PhysicalWater CHANGELOG

## 0.3.17 - Authoritative Physical Ocean Cutover

This patch is a deliberate architecture break from the compatibility-era water stack. PhysicalWater is now the only active water system while enabled.

### Removed vanilla water from the active stack
- WaterVolume and water LiquidSurface renderers remain suppressed for the session.
- WaterVolume.UpdateFloaters is suppressed while PhysicalWater is enabled.
- Water/LiquidSurface trigger updates are suppressed for water.
- Floating water queries are answered by PhysicalWater only.
- PhysicalWater no longer adopts Valheim water materials.
- PhysicalWater no longer adopts Valheim water audio.
- Rendering failure never revives vanilla water as a fallback.

### Exclusive ship and floating-object physics
- Ship.CustomFixedUpdate is suppressed while PhysicalWater owns the water simulation, removing the 0.3.16 double-buoyancy launch path.
- Floating.CustomFixedUpdate is suppressed for PhysicalWater-controlled floating bodies.
- Ships use one six-point hull solver sampling PhysicalWater height, normal, and water velocity.
- Total buoyancy is bounded near gravity instead of stacking >2x gravity over Valheim flotation.
- Relative water drag, angular damping, angular-velocity clamps, and rigidbody velocity clamps prevent stored launch energy.
- Sail, paddle and rudder forces are supplied by PhysicalWater with bounded acceleration-based forces.
- Ship wakes inject bounded disturbance into the shared ripple field.

### Stable character water state
- Character swimming remains normal Valheim locomotion, but its waterline is supplied only by PhysicalWater.
- Character liquid state uses a low-pass PhysicalWater surface rather than the full high-frequency ripple field, preventing the violent vertical bouncing seen in 0.3.16.
- Physical water waves continue independently for boats, objects, rendering and interactions.

### One wave field for physics and rendering
- The UnityPhysicalOcean shader now performs bounded GPU vertex displacement from the same four deterministic wave layers used by CPU gameplay physics.
- Wave direction follows the PhysicalWater wind/storm state.
- The CPU mesh topology remains stable: no CPU jelly-sheet deformation loop.
- Physical wave amplitude is pushed into the shader every frame.

### Live physical ripple rendering
- The CPU ripple simulation is uploaded to a live 161x161 RGBA ripple texture at up to 20 Hz.
- The shader samples that field in the vertex stage for visible local surface displacement.
- The fragment stage samples ripple gradients to perturb normals, so disturbances affect highlights and surface shape.
- Player, object and boat disturbances therefore feed both gameplay physics and the visible ocean.
- Ripple displacement and ripple velocity are hard-clamped to prevent runaway feedback.

### Water visibility hardening
- The UnityPhysicalOcean material remains the preferred and release-quality surface.
- Bundle loading can recover the custom shader directly from the AssetBundle if the material asset lookup fails.
- Replacement renderers explicitly disable shadows, occlusion culling and motion vectors that can interfere with a large dynamic ocean surface.
- If the custom bundle shader cannot be used, PhysicalWater creates an emergency self-owned blue surface material so the ocean cannot become invisible. This is NOT a vanilla-water fallback and is logged as a release-blocking error.
- Surface alpha was increased so a valid custom shader cannot disappear into the scene through excessive transparency.

### Effects and audio
- Existing splash, foam, shoreline foam and wake effects remain driven by PhysicalWater interactions.
- Shoreline effect cadence/budget increased for stronger visible coastal activity.
- PhysicalWater now synthesizes its own looping ocean ambience and splash clip at runtime.
- No Valheim water audio clip is adopted.

### 0.3.17 installed tuning
- WindWaveAmplitude: 0.78
- MaxSimulatedDisplacement: 0.24 m
- Ripple WaveSpeed: 5.8
- Ripple Damping: 0.942
- CharacterDisturbanceScale: 0.22
- ObjectDisturbanceScale: 0.18
- ShipWakeStrength: 0.035
- ShorelineEffectsInterval: 0.16 s
- ShorelineEffectsBudget: 220

### Non-negotiable architecture
- PhysicalWater owns water height, ripple state, wave normals/velocity, buoyancy, wakes, water visuals and water interaction effects.
- Vanilla WaterVolume may exist as a disabled scene object only so Harmony can neutralize it. It does not render, float objects, drive triggers, provide water height, supply a material, or supply audio.
- Future work builds on this architecture. Do not restore a second vanilla water solver to mask failures.
