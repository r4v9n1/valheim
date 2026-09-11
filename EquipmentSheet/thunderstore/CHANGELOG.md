# Changelog

## 1.0.1

- Fixes Valheim 1.0 inventory persistence notifications by using the current two-boolean `Inventory.Changed` signature.
- Fixes the equipment-upgrade hook to target Valheim 1.0's current `Inventory.AddItem` overload.
- Replaced the package icon with a custom-made, non-AI-generated icon.

## 1.0.0

- Rebuilt against the current Valheim 1.0 assemblies.
- Preserves the existing equipment, food-slot, upgrade, save, death-drop, and tooltip behavior.

## 0.9.3

- Moves the human/AI development disclosure to the top of the Thunderstore details text so it is visible immediately.
- Adds a concise human-directed, AI-assisted development note to the package manifest description.
- Corrects the Thunderstore package layout so the DLL is placed under `plugins/EquipmentSheet/EquipmentSheet.dll`.
- No gameplay changes intended relative to 0.9.2.

## 0.9.2

- Adds the human/AI development disclosure to the Thunderstore package description.
- No gameplay changes intended relative to 0.9.1.

## 0.9.1

- Fixes manually removing gear from the sheet after automatic equipment routing was added.
- Dragging gear from a sheet slot into the bag now leaves it in the bag and unequipped instead of routing it straight back to the sheet.
- Right-clicking an occupied equipment slot now unequips the item and returns it directly to the bag when a slot is available.
- Keeps the 0.9.0 upgrade-list, in-slot upgrade, and automatic gear-replacement behavior.

## 0.9.0

- Equipped sheet gear now appears in crafting-station Upgrade lists and can be upgraded without moving it into the bag first.
- A successful upgrade returns the higher-quality replacement to the same equipment slot and equips it automatically.
- Equipping supported gear from the bag now moves it into its matching sheet slot automatically.
- Replacing equipped gear swaps the previous sheet item into the bag cell vacated by the newly equipped item, so the operation also works with a full bag.
- Failed upgrade creation rolls the original sheet item back into its slot.

### Updating from 0.8.1, 0.9.0, or 0.9.1

- Close Valheim completely before replacing the old DLL or updating the package.
- You do not need to remove gear from the equipment sheet before updating. Version 0.9.2 uses the same saved equipment data and slot layout.
- Keep `BepInEx/config/r4v9n1.equipmentsheet.cfg`. Deleting it only resets the panel-position settings to their defaults; it does not remove or reset saved sheet gear.
- Move all sheet gear back into the normal bag only if you intend to uninstall EquipmentSheet entirely.
- A character backup before updating mods is always a sensible precaution, but it is not required for this update.

## 0.8.1

- Fixes food-slot removal: left-click picks up the stack and right-click consumes one item through Valheim's normal item-use path.
- Packages EquipmentSheet for Thunderstore under the `R4V9N1` creator identity.
- Uses the `r4v9n1.equipmentsheet.cfg` BepInEx config filename.
