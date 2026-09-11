# BrennivinProtection

Current release: **1.0.0**, rebuilt against the current Valheim 1.0 assemblies.

BepInEx/Jotunn mod for Valheim that adds Brennivín, a mead-kettle protection flask.

## Behavior

- Carry at least one Brennivín flask to die normally but keep inventory items and equipped gear.
- One flask is consumed automatically when death protection activates.
- No protected inventory should be moved into a tombstone.
- Drinking a Brennivín flask teleports the player to their claimed bed.
- Drinking does not consume the flask if no claimed bed exists or teleport cannot start.
- The flask and mead base are cloned from existing vanilla mead assets and tinted cyan blue.
- Equipment Sheet is supported reflectively for death handling when possible.
- Jotunn requires the mod on server and clients with matching patch version.
- Gameplay config entries are marked server/admin controlled for Jotunn config sync.
- After bed teleport, the mod attempts to pulse Valheim's wake-up animation.

## Recipe

Craft one `Mead base: Brennivín` at the Mead Ketill:

- Cloudberries x50
- Honey x50
- Thistle x50
- Dandelion x50
- Bone Fragments x50

Ferment the base in a Fermenter to produce one Brennivín flask.

## Build

```powershell
.\build.ps1 -ValheimDir "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Output:

```text
dist\BrennivinProtection.dll
```

## Install Locally

```powershell
.\install.ps1
```

This copies the DLL to:

```text
<Valheim>\BepInEx\plugins\BrennivinProtection\BrennivinProtection.dll
```

## Package

```powershell
.\package.ps1
```

Package output:

```text
artifacts\R4V9N1-BrennivinProtection-0.1.3.zip
```
