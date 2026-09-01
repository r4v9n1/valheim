# PhysicalWater 0.6.0-devB5

## Stage B.1 long-run and particle-density validation

- Preserves the devB4 six-test Stage-B FLIP/PIC regression suite unchanged.
- Adds `VolumetricFlipDensityDiagnostics` for validation-only readback of particle occupancy per MAC cell.
- Adds a 60-second closed-tank soak with snapshots at 0, 10, 30 and 60 seconds.
- Adds particle-clustering health metrics: occupied cells, mean particles per occupied cell, maximum cell occupancy, singleton/sparse/crowded fractions.
- Adds repeated dynamic-solid and center-wall insertion/removal stress cycles while enforcing particle count, represented volume, finite state, no particles in solids and no particles outside the domain.
- Adds a dedicated Stage-B1 one-command Unity batch validator. It does not install or modify the Valheim plugin.
- No APIC affine transfer, reseeding, SDF voxelization or marching-cubes rendering is claimed in this revision. devB5 is intended to establish the long-run/density baseline that those later changes must improve without breaking devB4 physics.
