# LiquidCore compatibility identities

LiquidCore is the canonical product, project-directory, project-file, assembly,
DLL, package, and installed plugin-directory identity as of 2026-08-31.

The following `PhysicalWater` identities are intentionally retained:

- BepInEx plugin GUID: `r4v9n1.physicalwater`
- BepInEx config file: `r4v9n1.physicalwater.cfg`
- all existing config sections and keys, including `PhysicalWaterInteractionEnabled`
- Harmony owner identity, because it uses the BepInEx GUID
- `pw_*` console commands and aliases used by existing validation/live procedures
- Unity asset bundle filename/lookup identity: `physicalwater_assets`
- existing `PhysicalWater` C# namespaces, class names, and serialized/internal object names
- solver/validation package identifiers and historical filenames where changing them
  would add risk without changing the public product identity
- Git remote repository name `r4v9n1/PhysicalWaterUnity`
- PhysicalWater-era release archives, diagnostics, reports, and historical documents

These retained names protect existing configuration, scripts, assets, installed
worlds, multiplayer/runtime expectations, and forensic reproducibility. They are
not stale product branding.

The maintained installer writes `LiquidCore.dll` to
`BepInEx\plugins\LiquidCore` and removes obsolete `PhysicalWater.dll` copies
from the BepInEx plugin tree. It does not remove the legacy config file or user
persistent data.
