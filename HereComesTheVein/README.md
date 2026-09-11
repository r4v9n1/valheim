# HereComesTheVein

> I use AI to review code and assist with optimization and integrity; I manually test and review every mod, and all decisions and code remain my own.

A lightweight Valheim mod by R4V9N1.

HereComesTheVein keeps vanilla copper veins unchanged and adds weighted IronOre drops to them.

## Behaviour

- Existing copper vein drop tables receive a weighted IronOre entry alongside CopperOre.
- The configured weighting averages approximately **60% CopperOre and 40% IronOre**.
- Vanilla copper vein models, names, mining behavior, quantities, and world-generation identity remain unchanged.
- The server applies the loot change authoritatively; clients may also install the mod for matching setup and diagnostics.
- Existing worlds are supported because the mod does not regenerate or rewrite world-generation data.

## Multiplayer

Install the mod on the server/host **and on every client**.

## Requirements

- Valheim
- BepInExPack Valheim

## Manual installation

Copy `HereComesTheVein.dll` into:

`Valheim\BepInEx\plugins\HereComesTheVein\`

## Removal

Removing the DLL stops the runtime loot augmentation. The mod does not rewrite vanilla copper-prefab identities or world-generation data.

## Technical approach

The plugin patches Valheim's streamed `ZNetScene.CreateObject` path. It recognizes `rock4_copper` and `rock4_copper_frac`, deterministically selects a minority by position, recolors only that instance, and swaps `CopperOre` entries in the instance mining drop table to `IronOre`.
