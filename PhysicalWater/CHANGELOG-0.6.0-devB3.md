# PhysicalWater 0.6.0-devB3

Stage-B validation geometry correction.

## Why devB2 failed
- Four core transport tests passed: falling column, wall containment, dam break, and vertical drain.
- The dynamic-solid block was positioned too high after the validation tank had settled, so the test could create a nominal cavity in already-dry space.
- The unequal-chamber probe/window combination measured high splash occupancy more than pressure-driven mass transfer; devB2 still moved 202 particles to the right chamber.

## Changes
- Move the validation block down to a fully submerged location at roughly one-sixth of domain height.
- Probe the exact 5x5x5-cell block interior.
- Require evidence that the block region contains water before insertion, that insertion evacuates that interior without deleting particles, and that removal refills it.
- Move the chamber connection to a fully submerged 3-cell-high by 7-cell-wide opening.
- Measure both total right-chamber particle gain and a low downstream receiving band instead of using a high splash band.
- Extend the equalization observation window to 240 steps and cavity refill to 150 steps.
- Conservation, finite-state, no-particle-in-solid, and max-velocity gates remain enforced.
- No Valheim installation changes.
