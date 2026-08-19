# LightMyFire - Coal&Resin

## Current identity

- In-game mod name: **LightMyFire**
- Thunderstore package name: `LightMyFire_Coal_Resin`
- Public title: **LightMyFire - Coal&Resin**
- Plugin GUID: `r4v9n1.lightmyfire`
- Publish-ready version: `0.5.0`
- Creator/team: `R4V9N1`

The Thunderstore package identity was changed after earlier package-name/version submission collisions. The internal plugin identity remains `LightMyFire`.

---

## Purpose

LightMyFire adds two Valheim fuel-storage barrels:

### Coal Barrel
- Accepts **Coal only**
- Automatically tops up nearby compatible Coal-burning light sources

### Resin Barrel
- Accepts **Resin only**
- Automatically tops up nearby compatible Resin-burning light sources

Everything else is rejected.

---

## Main gameplay rules

### Strict fuel filtering

Hard rule:

- Coal Barrel accepts only vanilla Coal
- Resin Barrel accepts only vanilla Resin
- all other items are rejected
- removal is always allowed

Filtering is enforced across current Valheim inventory insertion/check paths, with an owner-side sanitation fallback for unusual bypasses or future compatibility.

### Automatic refill

- Default range: **50 m**
- Default interval: **5 minutes**
- Lights are topped up while partially fueled, not only when empty
- Compatible loaded `Fireplace` sources using vanilla Coal or Resin are targeted
- Finite fuel capacity is required

### Multiplayer/network behavior

The barrel owner is authoritative for real fuel availability.

A matching light within range can request fuel, and the peer that owns the barrel decides how much fuel is actually available. This avoids skipping lights because another peer has a stale local copy of the barrel inventory.

Use the same LightMyFire version on:
- dedicated server
- all connected clients

---

## Barrel implementation

The current working barrels use Jotunn's supported vanilla-prefab cloning path instead of a raw Unity clone.

This fixed earlier issues including:

- `Container.Awake()` null references
- `WearNTear.Awake()` null references
- broken hover text
- inability to open
- inability to damage/dismantle
- placement-ghost problems

The clone preserves native Valheim components including:

- `Piece`
- `Container`
- `WearNTear`
- `ZNetView`
- native collider hierarchy

Custom lid/plaque visuals are decorative only.

---

## Visuals

The barrel uses the native Valheim barrel body plus:

- forged metal lid
- wooden front plaque
- forged plaque frame
- COAL / RESIN labeling

A previous mirrored-text bug made `COAL` appear as `LAOC`; this was corrected.

---

## Range marker

Intended behavior:

- thin white dotted/spotted circumference
- located exactly at the configured feed radius
- default radius: **50 m**
- visible while positioning the barrel placement ghost
- visible while that barrel inventory is open
- hidden otherwise
- dynamically follows configured range

The range marker has gone through several iterations and should remain on the regression-test list.

A previous invisible-ring issue was caused by triangle winding/back-face culling.

---

## Build recipe

Current source recipe:

- Wood ×100
- Iron ×40
- Tar ×40

Placement:
- Workbench
- Hammer → Misc

If build cost appears incorrect, also check Valheim no-cost/dev settings and inventory-linking mods before assuming the recipe is missing.

---

## Dependencies / tested environment

Expected Thunderstore dependencies:

- `denikson-BepInExPack_Valheim-5.4.2333`
- `ValheimModding-Jotunn-2.29.2`

Environment used during development included:

- Valheim `0.221.12`
- Unity `6000.0.61`
- BepInEx `5.4.23.3`
- Jotunn `2.29.2`

Re-check current compatibility before future releases.

---

## Build workflow

Typical workflow:

1. Close Valheim for a full build.
2. Extract the current source/build package.
3. Run:

```text
BUILD-AND-INSTALL.bat
```

The build pipeline should:

1. rebuild the Unity AssetBundle
2. compile `LightMyFire.dll`
3. validate package contents/version
4. install the local client DLL
5. generate the Thunderstore artifact

Expected current artifact name:

```text
artifacts\R4V9N1-LightMyFire_Coal_Resin-0.5.0.zip
```

Upload that generated artifact unchanged.

---

## Thunderstore package structure

Required ZIP root:

```text
manifest.json
README.md
CHANGELOG.md
icon.png
plugins/LightMyFire/LightMyFire.dll
```

Important:

- no extra enclosing root folder
- ZIP paths must use `/`
- avoid Windows-style `plugins\LightMyFire\...` entries
- icon should be valid 256×256 PNG
- version should use three-part semantic versioning, e.g. `0.5.0`
- do not reuse an existing package/version pair

The package script was updated to explicitly generate forward-slash ZIP paths and validate the archive.

---

## Thunderstore history

Earlier submission attempts produced generic errors such as:

```text
Package rejected
Invalid submission
```

Problems encountered included:

- Windows backslash path inside ZIP
- package/version already existing
- old package identity becoming awkward to reuse

The successful solution was to publish under:

```text
LightMyFire_Coal_Resin
```

while preserving the internal plugin identity `LightMyFire`.

---

## Useful diagnostics

Healthy startup lines may include:

```text
Loading [LightMyFire <version>]
Strict fuel-only item filter attached to <N> current Inventory insertion/check method(s).
Loaded custom coal and resin barrel models from the embedded Unity AssetBundle.
Using Valheim barrel base prefab 'piece_chest_barrel'.
Prepared 'Coal Barrel' as a Jotunn native clone...
Prepared 'Resin Barrel' as a Jotunn native clone...
Registered the coal and resin barrels...
Registered LightMyFire cross-peer fuel grant RPC.
```

Refill diagnostics may include:

```text
Runtime-ready coal barrel '...' with <N> fuel item(s).
Refill scan: fireplaces=..., owned=..., compatible=..., needFuel=..., inRange=..., requests=...
```

These help distinguish:

- barrel registration issues
- compatibility issues
- range issues
- ownership/network issues
- empty-barrel issues

---

## Important fixes already made

### Barrel registration race
Earlier code could initialize too early before a valid ZDO/inventory existed.

Fix:
- retry runtime setup
- recover loaded barrels if the active registry is empty

### Light compatibility
Earlier logic depended on `m_canRefill`, excluding otherwise compatible lights.

Fix:
- detect support from actual fuel item + finite capacity

### Native prefab name changes
Historical name:

```text
piece_chestbarrel
```

Current name encountered:

```text
piece_chest_barrel
```

Fix:
- dynamic base-prefab resolution

### Raw clone failure
Raw `UnityEngine.Object.Instantiate(baseBarrel)` caused broken native references.

Fix:
- Jotunn `CustomPiece(name, baseName, PieceConfig)` clone path

### Multiplayer refill skipping
Some lights were skipped despite enough fuel.

Fix direction:
- do not trust stale local barrel inventory state
- request fuel from in-range barrel
- barrel owner decides actual available quantity

### Mirrored plaque
Corrected COAL/RESIN orientation.

### Invisible range ring
Corrected ring-face orientation / rendering logic after back-face culling made the marker invisible.

---

## Regression test checklist

### Coal Barrel
- placement ghost stable
- places successfully
- hover text works
- opens with E
- accepts Coal
- rejects Resin
- rejects unrelated items
- Coal can be removed
- can be damaged
- can be dismantled

### Resin Barrel
- places successfully
- opens
- accepts Resin
- rejects Coal
- rejects unrelated items
- Resin can be removed
- can be damaged/dismantled

### Refill
- multiple partially empty matching lights within 50 m
- enough fuel remains in barrel
- every compatible in-range light is considered
- outside-range lights remain untouched
- wrong-fuel barrel does not refill
- refill occurs on 5-minute schedule
- dedicated-server/multiplayer behavior works

### Visuals
- lid aligned
- plaque aligned
- COAL reads normally
- RESIN reads normally

### Range marker
- appears during placement
- appears while inventory is open
- sits at configured range
- hides afterward
- remains visible on uneven terrain

### Logs
There should be no recurring:

```text
Container.Awake NullReferenceException
WearNTear.Awake NullReferenceException
Container.GetHoverText NullReferenceException
Container.Interact NullReferenceException
WearNTear.GetSupport NullReferenceException
WearNTear.GetHealthPercentage NullReferenceException
```

---

## Main project areas

Typical source files:

```text
src/LightMyFirePlugin.cs
src/LightMyFireBarrel.cs
src/LightMyFireHarmonyPatches.cs
```

Unity visual builder:

```text
unity/LightMyFireModels/Assets/Editor/BuildLightMyFireModels.cs
```

Thunderstore:

```text
thunderstore/manifest.json
thunderstore/README.md
thunderstore/CHANGELOG.md
thunderstore/icon.png
```

Build/package:

```text
BUILD-AND-INSTALL.bat
build.ps1
rebuild-all.ps1
package.ps1
```

---

## Rules for future changes

1. Do not replace the Jotunn native clone with a raw Unity prefab clone.
2. Keep decoration visual-only and collider-free.
3. Keep strict Coal/Resin acceptance.
4. Keep item removal allowed.
5. Keep barrel-owner authority for fuel availability.
6. Do not trust stale remote inventory state to decide whether to send a refill request.
7. Keep refill logic generic for compatible Coal/Resin `Fireplace` sources where practical.
8. Preserve native Valheim/Jotunn save/network behavior.
9. Generate Thunderstore packages automatically instead of manually re-zipping.
10. Increment the published version for every Thunderstore update.

---

## Current status

The mod reached a working state where:

- barrels place correctly
- barrels open correctly
- visuals are acceptable
- strict fuel filtering works
- damage/dismantling works
- automatic refill works
- multiplayer refill logic was improved
- default refill interval is 5 minutes
- the fresh Thunderstore package identity successfully published

The range-marker feature remains the main item to verify carefully after future changes.

---

## Future ideas

Possible additions:

- improved range-marker visuals
- in-game barrel status
- refill statistics
- server-admin diagnostics
- configurable extra fuel types
- compatibility metadata for modded light sources
- another visual/material polish pass

Core identity should stay simple:

**One Coal barrel, one Resin barrel, automatic nearby refilling, minimal fuss.**
