# PhysicalWater 0.4.4 - Bathymetry + Continuous Surf

## Milestone goal
Turn the successful 0.4.1 LOD ocean into a water body that understands bottom depth and static obstructions instead of painting shoreline effects on top of an unaware surface.

## Local bathymetry and obstruction field
- Added a 161x161 player-following bathymetry texture aligned with the existing 120 m local physics field.
- R stores effective water depth up to 64 m.
- G stores continuous distance-to-dry-shore influence.
- B stores continuous distance-to-static-obstacle influence.
- A stores seabed/effective-bed slope.
- Bathymetry is rebuilt only when the local origin/mask changes, not every frame.
- Terrain height remains the base bed. In water shallower than 10 m, non-trigger static colliders intersecting the water column are sampled and can raise the effective bed. Dynamic rigidbodies are excluded so boats/creatures do not become coastline.
- Emergent rocks/static geometry become local dry boundaries. Submerged rocks create shallow-water and obstacle influence instead of being ignored.

## Shallow-water wave response
- CPU and GPU spectrum amplitudes now share the same depth attenuation rule.
- Long wavelengths begin feeling the bottom when depth approaches roughly half wavelength, following the same practical rule used by established ocean systems.
- Intermediate shallows receive a small shoaling/steepening band before strong surf-zone attenuation.
- Very shallow water suppresses large geometric displacement so waves do not drag a sheet through beaches and rocks.
- Local dynamic ripple propagation slows with depth and receives additional surf-zone damping. Dry/static obstacle cells reflect the local wave equation instead of transmitting through them.

## Continuous shoreline and rock interaction
- Removed normal-operation shoreline particle emission, foam decals and shoreline quad overlays.
- Shore foam is now a continuous shader field driven by bathymetry depth, shore distance, seabed slope, spectral crest energy and an incoming-wave phase.
- Static obstacle proximity adds localized impact foam around rocks/piers without spawning glowing dots or diamonds.
- Scene depth remains in the optical path for per-pixel rock/terrain depth and is combined with the physical bathymetry depth.

## Smooth interaction field
- Ripple texture upload raised from 20 Hz to 30 Hz.
- Shader uses a weighted 9-tap reconstruction before deriving ripple slope/foam, hiding the visible 1.5 m simulation cells.
- Persistent interaction visuals are GPU-owned. Legacy CPU foam/splash surface sprites are disabled; proper airborne spray can be rebuilt later as a dedicated effect.

## Origin continuity
- Moving the player no longer clears all local ripple state when the 48 m follow-grid snap changes.
- Overlapping portions of current/previous/next simulation buffers are shifted into the new origin, preventing large visual resets while traversing the coast.

## Preserved foundation
- 0.4.1 concentric LOD ocean architecture remains.
- No vanilla Valheim water rendering, materials, flotation or WaterVolume simulation is restored.
- Boat solver is not redesigned in this milestone.
- Character wave-follow behaviour remains deferred until the water/ocean milestone is accepted.

## Research basis
- Crest sea-floor depth architecture: shallow wave attenuation, shallow shading and shoreline foam all consume explicit depth information.
- Crest dynamic waves: local interaction simulation is layered on top of environmental waves and stability depends on simulation frequency, damping and Courant-like speed limits.
- Finite-depth wave behaviour: the seabed begins influencing a wave before the shoreline; waves slow/shorten/steepen as depth falls and then dissipate/break in very shallow water.

## Next validation
- Shoreline should be continuous with no repeated white dots, square splats or diamond decals.
- Waves should visibly lose deep-ocean amplitude as they enter very shallow water.
- Submerged/emergent rocks should create local attenuation/foam rather than water passing visually through them unchanged.
- Swimming ripples should be smoother and no longer expose 1.5 m grid cells.
- Traverse more than 48 m along shore and verify ripple/shore visuals no longer reset abruptly.
