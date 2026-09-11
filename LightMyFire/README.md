# LightMyFire - Coal&Resin
**Version 1.0.0 is the Valheim 1.0 compatibility release.**
> **0.5.4 note:** automatic barrel refills no longer play the light source's fuel-added sound or visual effect.
> **0.5.4 note:** automatic barrel refills no longer play the light source's fuel-added sound or visual effect. The package description notes AI-assisted code analysis, optimization review, and verification support.
> **0.5.1 correction:** the published 0.5.0 package accidentally contained the wrong DLL. This source package is tied to the known-good 0.5.0 DLL you supplied: its managed code matches, and its exact embedded production AssetBundle is preserved for the 0.5.1 rebuild.


LightMyFire adds separate placeable **Coal Barrel** and **Resin Barrel** pieces. Each is cloned from Valheim's current native barrel/container prefab through Jotunn so the original networking, interaction, collider, and WearNTear behavior stays intact. LightMyFire then adds a compact custom metal lid and a small COAL/RESIN plaque as visual-only decoration. The inventory is 8x4 (32 slots).

## Development note

AI-assisted tools support code analysis, optimization review, implementation refinement, and verification support.

Created and maintained by **R4V9N1**.

## Behavior

- Both barrels appear under Hammer > Misc and require a nearby workbench.
- Each barrel costs 100 Wood, 40 Iron, and 40 Tar. The requirement is explicit in the PieceConfig and is consumed through Valheim's normal placement resource path.
- **Item restriction:** the Coal Barrel accepts only vanilla Coal; the Resin Barrel accepts only vanilla Resin. Every other item - including the other barrel's fuel - is rejected on drag/drop, shift-click, and quick-transfer ("move all"). An owner-side sweep also ejects (never deletes) anything that somehow still ends up in a barrel, dropping it in the world next to the barrel.
- Runs one shared, lightweight scan on a configurable interval (5 minutes by default) instead of patching each light's update loop, plus a short maintenance tick for transaction cleanup.
- **Top-up, not just refill:** every scan, LightMyFire tops up the *missing* fuel on any loaded, compatible light - not only fully empty ones. A light at 4/6 fuel receives 2; a full light consumes nothing.
- Services any loaded `Fireplace`-based light (vanilla or from another mod) whose fuel item is vanilla Coal or Resin - there is no hardcoded prefab list.
- **Nearest-first, multi-barrel top-up:** for each light, the nearest matching barrel with fuel is used first; if it can't fully cover what's missing, LightMyFire continues to the next-nearest matching barrel until the light is full or no matching fuel remains in range. Coal lights only ever draw from Coal Barrels, and Resin lights only from Resin Barrels.
- **Multiplayer-safe by construction:** a barrel's own owner is the only peer that ever removes or adds fuel in its inventory, and a fireplace's own owner is the only peer that ever applies fuel to it. Cross-peer requests use a transaction-ID'd request/grant/acknowledge/refund exchange with a bounded timeout, so a dropped connection, ownership change, or destroyed object can't duplicate or silently lose fuel.
- Compatible with Terramizer, TerramizerServer, EquipmentSheet, and InventoryLink 0.2.6 or newer.
- Must be installed on the server and every connecting client.

## Configuration

Config file:

```text
BepInEx/config/r4v9n1.lightmyfire.cfg
```

`Enabled`, `Feeding > Range` (default 50m), and `Feeding > RefillIntervalMinutes` (default 5) are **server-controlled**: Jotunn synchronizes them from the server/host to every connecting client, so a client's local config values for these three settings are overridden by whatever the server has configured. `Diagnostics > LogTransfers` is local-only and not synced.
`Diagnostics > LogDiagnostics` is also local-only. Enable it while testing to log runtime-ready barrels and a summary of each refill scan (found/owned/compatible/needs-fuel/in-range/requests).

## Build

Recommended one-command rebuild (regenerates the textured Unity AssetBundle, compiles the DLL, then packages it):

```powershell
.\rebuild-all.ps1
```

Manual equivalent:

```powershell
.\build-assets.ps1
.\build.ps1
.\package.ps1 -NoBuild
```

Outputs:

```text
dist\LightMyFire.dll
G:\My Drive\build\Valheim\releases\LightMyFire\R4V9N1-LightMyFire_Coal_Resin-1.0.0.zip
```

> **Build environment note:** `build-assets.ps1` requires Unity 6000.0.61f1 at the path configured in that script. `build.ps1` compiles against the **currently installed** Valheim, Jotunn, BepInEx and Harmony assemblies on the target machine. This is intentional: the 1.0.0 build must be compiled against the same current game/mod API that will actually run it. `BUILD-AND-INSTALL.bat` performs the Unity asset rebuild, DLL build, package validation and local test install in one pass.


## Thunderstore publishing

After a successful full rebuild, upload `G:\My Drive\build\Valheim\releases\LightMyFire\R4V9N1-LightMyFire_Coal_Resin-1.0.0.zip` to Thunderstore. See `THUNDERSTORE-PUBLISH.txt` for the package layout and checklist.
