# PhysicalWater 0.5.1 - Hydrodynamics Core Audit

0.5.1 is the mandatory pre-build correction to the first 0.5.0 hydrodynamics implementation. Do not test 0.5.0.

## Mass conservation fixes

- Replaced independent per-face transfer with a two-pass flux solver.
- Every cell first sums all requested outgoing transfers, then scales those transfers together so total outflow can never exceed owned water depth.
- Interior transfers are equal-and-opposite between donor and receiver. Only explicit open-ocean reservoir boundary cells may add or remove mass from the active local control volume.
- Building a new solid no longer deletes the water that occupied its footprint; displaced water is redistributed into nearby non-solid cells.

## Moving-domain correctness

- The 120 m hydrodynamic tile still preserves overlapping state when it snaps.
- Newly exposed cells are explicitly tracked. Genuine Ocean-biome cells in the new strip are reseeded from the ocean reservoir after geometry is rebuilt, preventing dry bands from appearing merely because the local window moved.
- This is active-tile state, not world persistence. Hydrodynamic state is not yet serialized to the world save and long-lived remote lakes/basins are a later persistence milestone.

## No blanket water paths

- Gameplay queries inside the active tile require actual local water depth at the nearest hydrodynamic cell. Bilinear sampling can no longer smear water through a dry cell or wall.
- Floating probes retain no nearby-water fallback.
- The far LOD shader now receives a separate coarse Ocean-biome domain texture. Outside the local hydrodynamic tile, the renderer fails closed unless the world domain is genuine Ocean biome.
- The old shader behavior that assumed all space outside the local bathymetry texture was open ocean is removed.

## Integration correctness

- Legacy `OverrideWaterQueries` can no longer re-enable Valheim water while PhysicalWater is enabled; missing replacement-system state fails dry/closed.
- Restored the terrain-height sampling helper accidentally dropped during the 0.5.0 rewrite; the pre-build audit caught this compile blocker before release.
- Authoritative Floating water-query hooks now fail closed on exceptions instead of returning control to Valheim's original water query. No silent vanilla-water fallback remains in those prefix paths.
- Water/ship/floater suppression Harmony patches are now required startup hooks. If a required hook cannot be installed, PhysicalWater refuses to initialize instead of quietly running beside vanilla water.
- Vanilla WaterVolume/LiquidSurface renderer suppression is unconditional whenever PhysicalWater is enabled; the old config toggle is retained only for compatibility.
- Camera underwater detection uses the same wet/dry authority as gameplay and cannot fabricate a SeaLevel surface in a dry cell.
- Surface normals treat dry neighbors as the current wet surface instead of sampling a fictitious global ocean.
- `GetWaterVelocity` now includes the shallow-water solver's horizontal face velocity, so later floating/player/boat interactions can feel actual flood/channel flow rather than only spectral wave motion.

## Walls and terrain

- Static collider face barriers remain authoritative for horizontal flow.
- New solid construction displaces existing volume instead of erasing it.
- Geometry is resampled every 0.65 s so placed/destroyed walls and terrain edits alter the flow topology without requiring the player to move.

## Interaction visual audit

- Removed continuous wind forcing from the local ripple solver. Wind/swell remains in the spectral ocean; local ripple energy is transient and now decays when interactions stop.
- Removed creation/emission of CPU foam and shoreline billboard systems from the active path.
- `EmitFoamDecal` is now a hard no-op, so frozen white discs/diamonds cannot persist even if an old/dead call site is accidentally reached.
- Ordinary player/object contact is represented only by the continuous ripple texture and shader foam field.
- CPU splash particles are limited to one or two short-lived droplets for only very high-energy impacts.
- The local ripple solver remains damped every fixed step and heavily damps very shallow cells; dry cells are zeroed, so interaction displacement has a defined decay path instead of becoming a static decal.

## Audit scope / known milestone limits

- 0.5.1 is a local real-time shallow-water milestone, not yet a persistent whole-world water save system.
- Existing ship solver code is carried forward but is not part of the 0.5.1 acceptance test; boat behavior remains a later subsystem milestone.
- First acceptance test: isolated pit remains dry, ocean-connected trench fills over time, solid wall blocks face flux, opening the wall permits flow.
