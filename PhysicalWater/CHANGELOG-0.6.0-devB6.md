# PhysicalWater 0.6.0-devB6

## Stage B.1 conservative density control

- Preserves the six devB4 physics regressions and all devB5 long-run/repeated-cycle gates.
- Adds conservative same-particle density redistribution. No particles are created or deleted and each particle retains its explicit represented volume.
- Adds `RedistributeCrowdedParticles`, using D3D11-safe atomic reservations on the existing per-cell count buffer.
- Density control runs every 4 simulation steps, targets at most 7 particles per cell, searches a two-cell neighbourhood, and uses two passes.
- Normal redistribution prefers already occupied under-populated liquid cells. Empty cells are only eligible during severe crowding and never above the donor cell, avoiding artificial upward surface inflation.
- Intended to correct devB5 results where the 60-second soak reached 24.7% crowded cells and repeated dynamic geometry produced a pathological 406-particle cell.
- No acceptance threshold is weakened.
- No APIC, split/merge reseeding, final SDF voxelization, or marching-cubes rendering is claimed yet.
