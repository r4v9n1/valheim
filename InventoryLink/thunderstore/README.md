# InventoryLink

InventoryLink is a client-side Valheim mod that links nearby accessible containers to building and crafting. Requirements show the combined materials available to you, and only the missing amount is pulled after placement or crafting succeeds.

The same DLL also includes safe, one-click sorting for player and container inventories.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

## Features

- Counts nearby chest contents for hammer building and normal workbench crafting or upgrades.
- Pulls only the shortfall after the action succeeds; your carried materials are used first.
- Searches within a configurable 50-metre radius and processes containers nearest-first.
- Respects wards, private-container access, and containers currently in use by default.
- Always excludes LightMyFire coal and resin automation barrels from linked crafting pulls.
- Gives the workbench, stonecutter, forge, black forge, Galdr table, and artisan table the same configurable build-range minimum (50 metres by default).
- Adds vanilla-style **Sort Inv.** and **Sort** buttons plus a configurable keyboard shortcut.
- Leaves equipped-capable items in their existing cells while organizing stackable items.

## Installation

Install with a Thunderstore mod manager, or copy `InventoryLink.dll` into:

```text
<Valheim>/BepInEx/plugins/InventoryLink/InventoryLink.dll
```

This is a client-side mod. Install it for each player who wants its building, crafting, and inventory UI features.

## Configuration

After the first launch, BepInEx creates:

```text
BepInEx/config/r4v9n1.inventorylink.cfg
BepInEx/config/r4v9n1.inventorylink.autosort.cfg
```

The auto-pull radius, access checks, extended station range, logging, sort buttons, button offsets, and sort shortcut are configurable.

## Notes

Recipes using Valheim's special “require only one ingredient” mode retain vanilla inventory behavior to avoid unsafe ingredient selection.
