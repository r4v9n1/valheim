# LightMyFire - Coal&Resin
## Release status

**0.5.1 is the corrected public release of LightMyFire.**

0.5.1 corrects a packaging mistake in 0.5.0 where the wrong DLL was included. No gameplay changes are intended relative to the known-good 0.5.0 DLL.

LightMyFire adds separate placeable **Coal Barrel** and **Resin Barrel** pieces to Hammer > Misc.

Each barrel has the same 8x4, 32-slot inventory space as a black metal chest and costs 100 Wood, 40 Iron, and 40 Tar. The Coal Barrel accepts only vanilla Coal; the Resin Barrel accepts only vanilla Resin - every other item is rejected. Fill one with its matching fuel and any compatible light within range gets topped up automatically on the next scan.

## Features

- Built from Valheim's current native barrel/container prefab through Jotunn, preserving its normal network, interaction, collider and WearNTear behaviour.
- Adds only visual decoration: a compact forged lid plus a small wood/metal COAL or RESIN plaque. The decoration has no collider and cannot steal clicks or damage hits from the barrel.
- **Coal-only / Resin-only inventories.** Wrong-fuel items (and everything else) are rejected on drop, shift-click, and quick-transfer; anything that slips through by another mod's path is safely ejected next to the barrel, never deleted.
- **Tops up, doesn't just refill.** Partially-fueled lights are topped up to their own maximum; full lights are left untouched.
- Works with any loaded `Fireplace`-based light source - vanilla or modded - that burns vanilla Coal or Resin and has finite fuel capacity, with no hardcoded torch/sconce list.
- Nearest-matching-barrel-first selection, falling through to the next-nearest barrel if one barrel can't fully cover a light's missing fuel.
- Transaction-safe multiplayer: fuel is only ever moved by the object's own owner, using a request/grant/acknowledge/refund exchange with timeouts, so ownership changes and disconnects can't duplicate or lose fuel.
- One efficient shared scan, 5 minutes by default, plus a lightweight maintenance tick for transaction cleanup.
- Configurable feeding range (50m default) and scan interval - **server-controlled**, synced to every client.
- A thin white dotted range ring marks the exact feeding-radius circumference while placing a barrel and while that barrel inventory is open. There is no filled area marker.
- Works in single-player and multiplayer.
- Compatible with Terramizer, TerramizerServer, EquipmentSheet, and InventoryLink 0.2.6 or newer.

## Installation

Install LightMyFire and Jotunn on the server and every connecting client. LightMyFire uses patch-level network version matching, so server and clients should run the same LightMyFire version.

## Configuration

Config file:

```text
BepInEx/config/r4v9n1.lightmyfire.cfg
```

`Enabled`, feeding range, and the refill interval are set by the server/host and synced to clients; a client's local values for those three are overridden. `LogTransfers` and `LogDiagnostics` are local-only. Enable `LogDiagnostics` while troubleshooting to see barrel readiness and scan counts.

Created and maintained by **R4V9N1**.
