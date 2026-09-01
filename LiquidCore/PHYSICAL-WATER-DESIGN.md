# Physical water design plan

## Target

Replace Valheim's vanilla water with an R4V9N1 physical water system that is:

- visible
- queryable by Valheim gameplay systems
- reactive to objects
- safe in multiplayer
- compatible with future world-generation/map-generation work

## What Valheim does today

Local inspection of `assembly_valheim.dll` shows the important water paths:

- `Floating.GetLiquidLevel(Vector3 p, float waveFactor = 1f, LiquidType type = LiquidType.All)`
- `WaterVolume.GetWaterSurface(...)`
- `WaterVolume.UpdateFloaters()`
- `Character.SetLiquidLevel(float level, LiquidType type, UnityEngine.Component liquidObj)`
- `Floating.SetLiquidLevel(...)`

Vanilla water is mostly a combination of water volumes, surface rendering, trigger overlap, liquid level calculation, and object/character consumers.

## Phase 0: replacement test prototype

Implemented in `0.1.2`.

- Runtime Unity mesh generated from C#
- Heightfield simulation around the local player
- Wind-wave procedural layer using `EnvMan`
- No collider
- No rigidbody
- Global `Floating.GetLiquidLevel` postfix
- Global `Floating.GetWaterLevel` postfix for ship-style water checks
- Global `Floating.IsUnderWater` postfix
- Floating-object disturbance
- Floating object liquid-level feed
- Character liquid-level feed
- Vanilla `WaterVolume` renderer suppression
- Vanilla `WaterVolume.UpdateFloaters()` suppression

Verification:

- Valheim launches with no BepInEx errors
- `PhysicalWater 0.1.2 loaded` appears in the log
- a blue water-preview mesh appears around the player
- the preview does not trap the player
- boats/items near the surface do not explode or jitter violently
- vanilla ocean visuals are gone
- characters and floating objects react to the PhysicalWater surface

## Phase 1: interaction polish

Goal: make the replacement test feel like water now that vanilla visuals are hidden.

Tasks:

- tune mesh material transparency and color
- add better foam/edge visual hints
- tune wave speed/damping
- add disturbances for player movement and projectiles
- test ships, dropped items, fish, swimming creatures, and carts near water
- measure performance at 65/97/129 grid sizes

Pass criteria:

- stable framerate
- no stuck player
- no runaway waves
- no recurring log spam
- boats and floating items continue behaving normally

## Phase 2: gameplay authority

Goal: let our water become the gameplay water source.

Tasks:

- enable and test `FeedCharactersLiquidLevel`
- verify swim start/stop
- verify stamina drain
- verify wet status
- verify drowning
- verify enemies and tames
- verify dedicated-server/client behavior

Pass criteria:

- clients agree on whether characters are in water
- no desync loops
- server remains authoritative for persistent gameplay state

## Phase 3: visual replacement

Goal: remove the vanilla water visuals without deleting vanilla world data.

Tasks:

- enable `HideVanillaWaterRenderers` in a test profile
- preserve vanilla colliders/triggers until physical gameplay replacement is proven
- compare coastlines, waves, lakes, ocean tiles, and storm conditions
- add a Unity-authored AssetBundle material/shader if runtime material is not good enough

Pass criteria:

- vanilla water surface is visually gone
- R4V9N1 physical water is visible and pleasant
- no missing-water holes
- no water visible through mountains/terrain in ugly ways

## Phase 4: map-generation integration

Goal: make generated worlds use the R4V9N1 water model as the main water field.

Tasks:

- inspect `WorldGenerator`, `Heightmap`, and terrain/water placement paths
- define a persistent water field per map zone
- support sea level, local basins, lakes, rivers, and connected ocean
- serialize only compact water parameters, not full per-frame simulation
- keep full runtime simulation local/visual unless synced state is required

Pass criteria:

- new worlds generate with predictable water placement
- existing worlds can opt in without corrupting saves
- dedicated servers do not need to simulate expensive visual water for every client

## Hard safety rule

Do not delete vanilla water objects or mutate save/world data during this test. The replacement path should remain config-recoverable until the prototype passes in-game testing.
