# Changelog

## 0.5.1

- Corrected the Thunderstore package: 0.5.0 accidentally shipped with the wrong DLL.
- The 0.5.1 build is generated from the same managed code as the known-good 0.5.0 DLL supplied by the author.
- Preserves the exact production Unity AssetBundle embedded in that known-good DLL.
- Build/package scripts now force a fresh 0.5.1 DLL and refuse to package a stale or mismatched build.
- No intended gameplay changes from the known-good 0.5.0 DLL.


## 0.5.0

- Published under the new Thunderstore package identity **LightMyFire_Coal_Resin** while keeping the in-game plugin identity **LightMyFire** unchanged.

- **First publish-ready public release.**
- Coal Barrel accepts only vanilla Coal; Resin Barrel accepts only vanilla Resin.
- Automatically tops up compatible Coal/Resin `Fireplace` lights within the configured range.
- Default refill scan interval is **5 minutes** and default feeding radius is **50 metres**.
- Multiplayer refill requests are owner-authoritative so lights are not skipped when barrel/light ownership is split across peers.
- Uses Jotunn's native prefab-cloning path for normal Valheim interaction, durability, networking and placement behaviour.
- Corrected COAL/RESIN plaque orientation and retained the visual-only forged lid/plaque decoration.
- Includes the thin white dotted range circumference while placing a barrel and while its inventory is open.
- Build pipeline generates a Thunderstore upload ZIP with metadata at ZIP root and normalized forward-slash ZIP paths.


## 0.2.11

- Corrected the mirrored COAL/RESIN plaque orientation.

## 0.2.10

- Improved dotted range-ring visibility and placement-ghost detection.
- Ring height is sampled around the circumference so uneven terrain does not bury the marker.

## 0.2.9

- Replaced the large workbench-style area effect with a thin white dotted circumference at the exact feeding range.
- The ring appears while positioning a barrel and while that barrel inventory is open, and remains hidden otherwise.

## 0.2.8

- Added the first visual feeding-range indicator tied to the synchronized feeding range.

## 0.2.7

- **All-sources refill fix:** refill scans no longer trust a non-owner peer's local copy of a barrel inventory. Matching in-range barrels are requested directly and the barrel owner authoritatively decides how much fuel is available. This fixes dedicated-server/multiplayer ownership splits where some lights were skipped despite fuel remaining.
- **Broader prefab hierarchy support:** Fireplace components may now resolve their ZNetView from the same object, a parent, or a child; grant application can also find Fireplace components below the network prefab root. This prevents otherwise compatible light prefab layouts from being silently skipped.
- Keeps the 0.2.6 native barrel prefab, strict Coal-only/Resin-only inventories, normal interaction/durability/build costs, and existing transaction safety.

## 0.2.6
- Fixes the current Valheim 0.221.x prefab integration bug that caused `Container.Awake`, `WearNTear.Awake`, hover, interaction and durability NullReferenceException spam.
- Creates each barrel with Jotunn's vanilla-prefab copy path instead of a raw Unity clone, preserving the native barrel's runtime/soft-reference dependencies.
- Validates Piece, ZNetView, Container, WearNTear and the native collider hierarchy before registering either custom piece.
- Re-fits the visual-only lid and plaque from the native barrel's physical collider bounds; imported decoration colliders are removed.
- Keeps the explicit Hammer recipe of 100 Wood, 40 Iron and 40 Tar and the 8x4 inventory.
- Keeps strict per-barrel fuel inventories: Coal accepts only Coal; Resin accepts only Resin, with the broad current-API insertion filter and owner-side fallback sanitation.
- Uses patch-level network version strictness for this compatibility build so a 0.2.6 client cannot silently run against a 0.2.5 server copy.
- Refreshes the custom lid/plaque source textures toward darker, rougher, less glossy Valheim-like wood and iron.

## 0.2.5
- Enforced strict per-barrel fuel inventories across current Inventory insertion/check paths.
- Added owner-side sanitation for wrong items inserted by another mod or an unpatched path.

## 0.2.4
- Rebuilt barrel decoration proportions around the actual Valheim barrel bounds.
- Smaller fitted forged lid and compact front plaque.
- Fixed mirrored label UV orientation.
- Reworked wood and iron PBR maps with grain, knots, cracks, pitting, scratches, oxidation and much lower smoothness.
- Preview proxy is now a bulged barrel silhouette; runtime still uses Valheim's native barrel body.

## 0.2.2
- Replaced procedurally generated barrel accessories with authored OBJ asset files.
- Added 1024x1024 PBR texture sets for hammered forged iron and carved wood plaques.
- COAL and RESIN labels are baked/carved into separate wood textures.
- Runtime still preserves Valheim's native barrel body mesh/materials.
- Added one-command build-and-install test script.


## 0.2.1

- **Refill initialization fix:** barrels no longer rely on a one-shot `Awake()` race with `ZNetView`. Runtime setup now retries until the ZDO/network view and inventory are ready, and an empty active-barrel registry can recover loaded barrel instances automatically.
- **Broader light compatibility:** Coal/Resin support is now determined by the actual `Fireplace` fuel item and capacity; `m_canRefill` no longer excludes otherwise compatible lights.
- **Diagnostics:** optional `Diagnostics > LogDiagnostics` reports barrel runtime readiness and refill-scan counts so a failed scan is no longer silent.
- **Visual upgrade:** the Unity builder now generates procedural albedo + normal maps for dark oak, amber oak, black iron, bronze, coal and resin instead of flat-colour materials.
- Added `rebuild-all.ps1` to rebuild the AssetBundle, DLL and Thunderstore package in one command.

## 0.2.0

- **Item restriction:** Coal Barrel now accepts only vanilla Coal and Resin Barrel only vanilla Resin, enforced on drag/drop, shift-click, and quick-transfer, with an owner-side sweep that safely ejects (never deletes) anything that gets in another way.
- **Top-up instead of empty-only refill:** lights are topped up to their own maximum fuel every scan; a light no longer has to reach zero first, and full lights consume nothing.
- **Generic Fireplace support:** any loaded `Fireplace`-based light burning vanilla Coal or Resin is serviced automatically, vanilla or modded, with no hardcoded prefab list.
- **Nearest-first, multi-barrel fallthrough:** if the nearest matching barrel can't fully cover a light's missing fuel, the scan continues to the next-nearest matching barrel.
- **Transaction-safe multiplayer:** fuel is only ever added or removed by an object's own ZNetView owner. Cross-peer fuel moves use a transaction-ID'd request/grant/acknowledge/refund RPC exchange with a bounded timeout and automatic refund, so ownership changes, disconnects, or destroyed objects can't duplicate or silently lose Coal/Resin.
- **Server-controlled config:** Enabled, feeding range, and refill interval are synced from the server/host to every client via Jotunn; clients can no longer run with different gameplay values than the server.
- Existing barrel prefab names, save data, and piece recipes are unchanged, so barrels placed under 0.1.0 continue to work after updating.

## 0.1.0

- Adds custom Unity-built coal and resin barrel models under Hammer > Misc, fitted to the vanilla barrel footprint and each with the same 32-slot capacity as a black metal chest.
- Shows or hides the modeled coal and resin contents automatically based on each barrel's synchronized inventory.
- Each barrel costs 100 Wood, 40 Iron, and 40 Tar.
- Efficiently refills matching empty coal- or resin-burning lights within 50 metres using one configurable timed scan every 15 minutes by default.
- Coordinates overlapping barrels so each light uses one matching barrel and scan-time fuel reservations distribute work safely.
- Supports native light capacities, persistent fuel storage, and multiplayer ownership.
- Compatible with Terramizer, TerramizerServer, EquipmentSheet, and InventoryLink 0.2.6 or newer.
