# PhysicalWater 0.6.0-devB2

## Stage-B D3D11 transport fix

- Split the original `ParticleToGrid` compute dispatch into `ParticleToGridU`, `ParticleToGridV`, and `ParticleToGridW`.
- Removed the 9-UAV D3D11 dispatch that Unity skipped entirely on the validation machine.
- Particle occupancy marking now runs separately from velocity scattering.
- Explicitly bind the Stage-A `ClearU`, `ClearV`, and `ClearW` kernels to their velocity textures.
- Preserve the Stage-A pressure/gravity core and all Stage-B conservation tests unchanged.
- Stage-B kernel parity after the split: 25 C# `FindKernel` names and 25 shader `#pragma kernel` declarations.

## Evidence from devB1 failure

Unity reported `ParticleToGrid` used 9 UAVs while the active D3D11 path supported 8, then skipped the dispatch. All FLIP validation scenarios consequently reported zero particle velocity and no transport. Unity also reported `_U`, `_V`, and `_W` were unset for the Stage-A clear kernels.
