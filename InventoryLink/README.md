# InventoryLink

BepInEx client-side mod for Valheim. InventoryLink lets building and workbench crafting requirements count nearby accessible containers, then pulls only the missing materials from those containers after a piece is placed or an item is crafted/upgraded.

## Development note

This mod is human-directed and built from human structure, concepts, ideas, client/server testing, and gameplay improvement goals. AI is used as an assisting tool for code analysis, optimization review, implementation refinement, and verification support, while final decisions, packaging, and in-game validation remain under human direction.

Created and maintained by **R4V9N1**.

Install it on the client that wants auto-pull. It is not a dedicated-server-only mod because Valheim's build UI and build requirement checks run client-side.

## Build

```powershell
.\build.ps1 -ValheimDir "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Output:

```text
dist\InventoryLink.dll
```

Create the upload-ready Thunderstore ZIP (this also builds the DLL):

```powershell
.\package.ps1
```

Package output:

```text
dist\R4V9N1-InventoryLink-0.2.8.zip
```

## Install

Copy the DLL to your client BepInEx plugins folder:

```text
<Valheim>\BepInEx\plugins\InventoryLink\InventoryLink.dll
```

Restart the game. BepInEx will create:

```text
<Valheim>\BepInEx\config\r4v9n1.inventorylink.cfg
<Valheim>\BepInEx\config\r4v9n1.inventorylink.autosort.cfg
```

The DLL bundles two BepInEx plugins (auto-pull and auto-sort below); both load from the single `InventoryLink.dll` file.

Defaults (`r4v9n1.inventorylink.cfg`):

```ini
[General]
Enabled = true
PullRadius = 50

[Crafting Stations]
ExtendBuildRange = true
BuildRange = 50
StationNames = $piece_workbench,$piece_stonecutter,$piece_forge,$piece_blackforge,$piece_magetable,$piece_artisanstation

[Access]
RespectWards = true
RespectContainerAccess = true
SkipInUseContainers = true

[Debug]
LogPulls = false
```

Defaults (`r4v9n1.inventorylink.autosort.cfg`):

```ini
[General]
Enabled = true
AddSortButtons = true
SortKey = LeftAlt+X
PlayerButtonOffsetY = 35
ContainerButtonOffsetY = 35
```

## Behavior

- Nearby chests count for hammer build requirements.
- Nearby chests count for normal workbench craft/upgrade requirements.
- Linked chest radius defaults to 50m.
- Workbench, stonecutter, forge, black forge, Galdr table, and artisan table build radii are raised to the same configurable 50m minimum by default.
- Hammer and workbench material counters show player inventory plus linked chest totals.
- Materials are removed from nearby accessible containers only after placement succeeds.
- Workbench materials are removed only after crafting/upgrading starts successfully.
- Player inventory is used first; InventoryLink pays only the shortfall from containers.
- Containers are sorted nearest-first.
- LightMyFire coal and resin automation barrels are always excluded from linked crafting pulls.
- Ward/private-container access is respected by default.
- Recipes that use Valheim's special "require only one ingredient" mode are left to vanilla inventory behavior to avoid unsafe item selection.

## Auto sort

Adds a "Sort Inv." button next to the player inventory panel — cloned from vanilla's own "Stack All"
button, so it matches the game's look without any hand-built UI — and a "Sort" button next to the
container panel, cloned from "Take All" and shown only while a container is open. Clicking either
re-packs that grid: equippable items (weapons, tools, shields, armor — anything you'd right-click
to equip) are left exactly where they are, and only the remaining stackable items (materials,
consumables, ammo, trophies, valuables, misc) are grouped by category, then name, then quality,
and reflowed into whatever empty cells are left. `SortKey` (default `LeftAlt+X`) is a keybind
fallback that sorts the player inventory and the open container (if any) in one press, in case a
cloned button ends up awkwardly placed at your UI scale — `PlayerButtonOffsetY`/`ContainerButtonOffsetY`
let you nudge the clones without recompiling.
