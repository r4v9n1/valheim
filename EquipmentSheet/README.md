# Equipment Sheet

Current release: **1.0.1**, rebuilt against the current Valheim 1.0 assemblies.

BepInEx client-side mod for Valheim. Adds a persistent equipment panel next to the vanilla player
inventory grid: Helm, Chest, Legs, Trinket, Back, Belt, plus three Food slots.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

Version `0.9.3` moves the human/AI development disclosure to the top of the Thunderstore details,
adds a concise disclosure to the package manifest description, and corrects the Thunderstore
package layout. No gameplay changes are intended relative to `0.9.2`.

Version `0.9.2` adds the human/AI development disclosure for publishing. No gameplay changes are
intended relative to `0.9.1`.

Version `0.9.1` fixes manual removal after automatic routing was introduced: dragging gear from a
sheet slot into the bag leaves it there unequipped, and right-clicking occupied equipment slots
returns their item directly to the bag when space is available.

Version `0.9.0` allows gear in the equipment sheet to appear in crafting-station Upgrade lists and
return to the same equipped slot after upgrading. Equipping supported gear from the bag now moves
it into its matching sheet slot automatically; replacing gear swaps the previous sheet item into
the exact bag cell vacated by the new item, including when the rest of the bag is full.

Version `0.8.1` fixes food-slot removal: left-click picks up the food stack, while right-click
consumes one item through Valheim's normal `Player.UseItem` path.

Version `0.8.0` adds three persistent food slots below the equipment slots. Drag food or drink
consumables into the slots to fill/refill them. Food slots are saved with the sheet, add normal
carry weight, and are moved into the vanilla inventory before tombstone creation so death/drop
server settings treat them like normal inventory food.

Version `0.7.0` makes the sheet behave more like the real character inventory: sheet gear is moved
into Valheim's normal inventory path before tombstone creation so death can drop it, loading a
different/new character clears stale sheet contents instead of reusing the previous character's
gear, occupied slots show vanilla item hover tooltips, and durability bars are drawn under durable
items.

Version `0.6.9` fixes the root cause of items never visibly staying in a slot: picking an item up
from the sheet correctly started a drag, but `InventoryGui.UpdateContainer()` runs every frame and
has a vanilla safety net that cancels any drag whose source inventory isn't the player's own bag
and isn't a currently-open container - the equipment sheet is neither, so it silently cancelled
every sheet-originated drag within the same frame it started, before the held-item icon could ever
be seen. The plugin now brackets that one check so it never fires for the sheet's own inventory.
Also fixes: a stuck-drag case where dropping an item back onto the exact slot it came from was
treated as "still occupied" and always failed instead of succeeding as a no-op; the repair-station
button silently repairing an already-full-durability sheet item as a false-positive match and
never reaching an actually damaged one; and the same slot-position collision on item eviction
described below.

Version `0.6.8` fixes items not actually equipping (no armor value, no visuals, reverting after
relogin) and slot clicks not registering reliably. Items placed in the sheet were only ever
getting a data flag flipped; Valheim's real equip state lives in per-character fields
(`m_chestItem`, `m_helmetItem`, etc.) that are populated by `Humanoid.EquipItem()`, which refuses
any item not physically inside the player's own bag inventory. The plugin now reconciles those
character fields by hand whenever the sheet's contents change or a character loads, so gear in the
sheet is genuinely worn. Slot hitboxes also now react to the same input event every other Valheim
inventory slot uses (mouse-down) instead of a stricter click event that could silently drop the
interaction on small mouse movement. An item-eviction edge case that could overlap and hide a bag
item was also fixed.

Version `0.6.7` replaces the unsafe expanded-player-inventory-row design with a separate
six-slot equipment inventory. That inventory is serialized into `Player.m_customData`, restored
when the character loads, rendered in the same right-side panel position, and included in the
player inventory weight calculation so it does not create a carry-weight benefit. Equipment-grid
drops and slot clicks are handled by direct slot hitboxes so each slot accepts only its matching
equipment type. Item transfer moves the original item object and rolls back on failure instead of
creating a clone and deleting the source item. A pending-transfer backup is written before any
source item is removed, so interrupted transfers can be restored to the player bag. Slot icons are
rendered directly from the equipment inventory instead of relying on `InventoryGrid` rendering.
Transfers use direct inventory-list movement to avoid Valheim add/remove side effects.

## Build

```powershell
.\build.ps1 -ValheimDir "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Output:

```text
dist\EquipmentSheet.dll
```

Create the upload-ready Thunderstore ZIP (this also builds the DLL):

```powershell
.\package.ps1
```

Package output:

```text
dist\R4V9N1-EquipmentSheet-0.9.3.zip
```

## Install

Copy the DLL to your client BepInEx plugins folder:

```text
<Valheim>\BepInEx\plugins\EquipmentSheet.dll
```

Restart the game. BepInEx will create:

```text
<Valheim>\BepInEx\config\r4v9n1.equipmentsheet.cfg
```

Defaults:

```ini
[General]
Enabled = true
PanelGap = 20
PanelExtraRightOffset = 60
```

## Behavior

- Slots: Helm, Chest, Legs, Trinket, Back, Belt, Food 1, Food 2, Food 3.
- The panel is positioned to the right of the vanilla player inventory panel using the same
  `PanelGap + PanelExtraRightOffset` logic as earlier builds.
- Equipment and food-slot contents persist through character custom data.
- Character switching does not reuse equipment from a previous character.
- Sheet items are handed to Valheim's normal tombstone/death-drop flow before a grave is created.
- Occupied sheet slots show vanilla item hover details.
- Durable equipment slots show a durability bar under the icon.
- Sheet item weight is added to the normal player inventory weight total.
- Left-clicking a filled food slot picks up the stack.
- Right-clicking a filled food slot consumes one item.
- Food slots accept stack refills from the player inventory.
- Equipping supported gear from the bag routes it into its matching equipment slot automatically.
- Equipping replacement gear swaps the previous sheet item back into the vacated bag cell.
- Gear in sheet slots appears in crafting-station Upgrade lists and remains in its slot after upgrading.
- Dragging gear from a sheet slot into the bag leaves it unequipped instead of automatically returning it to the sheet.
- Right-clicking an occupied equipment slot unequips its item and returns it directly to the bag when space is available.
- The mod does not auto-equip random bag items and does not scan your bag for recovery.
- Bow, ranged-style, weapon, shield, tool, and torch item types are excluded.

## Slot Types

- `Helm` accepts `ItemType.Helmet`.
- `Chest` accepts `ItemType.Chest`.
- `Legs` accepts `ItemType.Legs`.
- `Trinket` accepts `ItemType.Trinket`.
- `Back` accepts `ItemType.Shoulder`.
- `Belt` accepts `ItemType.Utility`.
- `Food 1`, `Food 2`, and `Food 3` accept `ItemType.Consumable` items with food, stamina, eitr,
  or consume-status-effect data.

## Important

This version intentionally does not use hidden extra player-inventory rows for persistence. Items
inside the panel live in the mod's own saved sheet inventory, not in unsafe backing rows.
