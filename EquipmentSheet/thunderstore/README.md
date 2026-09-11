# EquipmentSheet

> I use AI to review code and assist with optimization and integrity; I manually test and review every mod, and all decisions and code remain my own.

Current release: **1.0.1**, rebuilt against the current Valheim 1.0 assemblies.

EquipmentSheet is a client-side Valheim mod that adds a dedicated equipment and food panel beside the vanilla player inventory. It gives important gear a consistent home without hiding its weight or bypassing Valheim's normal equip and death-drop behavior.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

## Features

- Six dedicated equipment slots: Helm, Chest, Legs, Trinket, Back, and Belt.
- Three persistent food slots for food and drink consumables.
- Uses Valheim's real equipped-item state so armor values and character visuals work normally.
- Automatically moves supported gear into its matching sheet slot when equipped and swaps the previous item back into the bag.
- Shows equipped sheet gear in crafting-station Upgrade lists and returns upgraded gear to the same slot.
- Includes sheet contents in carry weight.
- Shows vanilla item tooltips and durability bars.
- Moves sheet items into the normal tombstone flow before death drops are created.
- Keeps equipment separate per character and clears stale data when switching characters.
- Supports drag-and-drop, left-click pickup, food-stack refills, and right-click food use.
- Dragging equipment into the bag leaves it unequipped; right-clicking an equipment slot returns it directly to the bag.

## Installation

Install with a Thunderstore mod manager, or copy `EquipmentSheet.dll` into:

```text
<Valheim>/BepInEx/plugins/EquipmentSheet/EquipmentSheet.dll
```

This is a client-side mod. Install it for each player who wants the extra panel.

## Updating from 0.8.1, 0.9.0, 0.9.1, or 0.9.2

Close Valheim completely, then replace the previous DLL or update the package through your mod manager.

- You do not need to remove gear from the equipment sheet. Version 0.9.3 uses the same saved equipment data and slot layout, so leave it equipped while updating.
- Keep `BepInEx/config/r4v9n1.equipmentsheet.cfg`. Deleting it only resets panel positioning to the defaults and does not affect saved gear.
- Move sheet gear into the normal bag before removing EquipmentSheet permanently. This is necessary for uninstalling, not for updating.
- A character backup is a reasonable general precaution before mod updates, but this update does not require one.

## Configuration

After the first launch, BepInEx creates:

```text
BepInEx/config/r4v9n1.equipmentsheet.cfg
```

You can enable or disable the panel and adjust its horizontal gap and extra right offset for other UI mods or different UI scales.

## Compatibility note

Equipment and food in the panel live in EquipmentSheet's own per-character saved inventory. The mod intentionally does not use hidden extra rows in the vanilla player inventory.
