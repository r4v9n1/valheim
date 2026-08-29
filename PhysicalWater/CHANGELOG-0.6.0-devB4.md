# PhysicalWater 0.6.0-devB4

## Purpose
Compile-only correction for the devB3 strengthened Stage-B validator. No physics geometry, thresholds, kernels, solver settings, or acceptance criteria are changed.

## Fixes
- Corrected chamber diagnostic counter types in `RunVolumetricStageBValidation.cs`.
- `ParticlesRightOfCenter` is now widened to `long` before subtraction, avoiding implicit `uint -> int` and `uint - int -> long` conversion errors.
- Validation/report and runner identifiers bumped from `devB3` to `devB4`.

## Regression policy
The devB3 submerged cavity geometry, submerged chamber opening, and strengthened assertions remain unchanged. Falling-column, wall-containment, dam-break, and vertical-drain tests remain mandatory.
