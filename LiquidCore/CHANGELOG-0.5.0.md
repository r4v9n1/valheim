# PhysicalWater 0.5.0 — Hydrodynamics Core

## Architectural break

0.5.0 removes the 0.4.x rule that equated “terrain below SeaLevel” with water. The local water body is now stored as conserved water depth plus horizontal face velocity. A cell becomes wet only when water mass reaches it.

## Hydrodynamic state

- Added per-cell conserved water depth.
- Added east/north face velocity fields driven by free-surface gradients and gravity.
- Uses a fixed 30 Hz shallow-water step with bounded CFL-style velocity/transfer limits.
- Local water surface is `effectiveBed + waterDepth`; spectral ocean waves and small ripple displacement are layered on top only where actual water exists.
- Character water queries now follow the local hydrodynamic surface instead of a fixed global SeaLevel rail.

## No blanket fill

- Isolated excavations below sea level start dry.
- Startup water is seeded only from genuine Ocean-biome cells on the local domain boundary, then flood-connected through non-solid cells.
- Newly uncovered/dry cells remain dry until water physically flows into them.
- `RequireOceanConnection` is forced true for compatibility, but 0.5.0 wetness no longer depends on the old candidate-mask copy path.

## Terrain and structures

- Terrain/static geometry is re-sampled every 0.65 seconds so building or destroying barriers updates the flow topology without moving the player.
- Static structures are rasterized as solid hydrodynamic cells where appropriate.
- Added horizontal face raycasts near the water surface so thin walls between grid cell centers can block flux.
- Water occupying a cell that becomes solid is displaced into reachable neighboring fluid cells rather than flowing through the new wall.
- Destroying/opening a barrier re-enables face flux so retained water can flood through the opening.

## Ocean boundary

- Open ocean remains the 0.4.1+ spectral concentric LOD renderer.
- The local solver treats only true Ocean-biome perimeter cells as a reservoir and relaxes them toward sea level over time.
- Inland basins can therefore hold levels independent of global sea level.

## Visual/interaction carry-forward

- Keeps the 0.4.5 shader, bathymetry, continuous surf, depth optics, weather integration, and no-vanilla-water cutover.
- Bathymetry depth now comes from actual hydrodynamic water depth rather than `SeaLevel - terrainHeight`.
- The local ripple solver remains a separate high-frequency interaction layer on top of the conserved water body.

## Scope

This is a real hydrodynamic foundation, not full 3D Navier–Stokes. It targets open ocean, shore flooding, trenches, basins, walls and large-scale free-surface flow efficiently enough for Valheim. Boats/fish/fine fluid-object coupling remain the next phase after the water body itself is validated.
