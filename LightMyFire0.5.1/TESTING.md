# LightMyFire 0.5.1 test checklist

This mod's transaction/ownership logic (item 5 onward) **cannot be meaningfully verified by
compiling alone** - it only shows itself under real network latency, ownership handoffs, and
disconnects on a dedicated server. Everything below should be run on an actual dedicated server
with at least two clients before this version is considered verified.

## 0. Setup

1. Install LightMyFire and Jotunn on the dedicated server and on every connecting client.
2. Start the server and confirm the log shows `LightMyFire 0.5.1 loaded`, the barrel registration
   message, and **no** `[LightMyFire] Could not patch ...` warnings. If any patch warning appears,
   note exactly which one - it tells you which item-filter or RPC path is not actually protected
   and needs a signature fix before the related test below can pass.
3. Repeat step 2 on each client's log.
> Fast troubleshooting: temporarily set `Feeding > RefillIntervalMinutes = 0.5`, `Diagnostics > LogDiagnostics = true`, and `Diagnostics > LogTransfers = true` on the peer you are observing. A healthy barrel should log `Runtime-ready ... barrel`, and each scan should report at least one compatible/in-range light before a transfer request is sent.

## 1. Basic build and visuals

4. Select both barrels in Hammer > Misc and confirm **no** `Container.Awake` or `WearNTear.Awake` exceptions appear while the placement ghost is created. Build both and confirm each costs 100 Wood, 40 Iron, 40 Tar and uses the native barrel footprint.
5. Aim at each placed barrel. Hover text must appear without exceptions, E must open the 8x4 inventory, and hammer damage/dismantle must work normally. The custom lid/plaque must stay attached to the barrel and must not create their own clickable/damageable collider.

## 2. Mod compatibility requirement

6. Connect a client **without** LightMyFire installed and confirm the server rejects it (network
   compatibility mismatch), matching the `EveryoneMustHaveMod` requirement.

## 3. Item restriction (repeat each on both a dedicated-server host and a regular client)

7. Try to place vanilla Resin into a Coal Barrel via: normal drag, shift-click from your inventory,
   and any "move all"/quick-transfer action. All three must be rejected; the Resin must remain in
   your inventory, not vanish.
8. Try to place vanilla Coal into a Resin Barrel via the same three paths. All three must be
   rejected.
9. Try an unrelated item (e.g. Wood) into both barrel types via the same three paths. Rejected in
   both.
10. Confirm Coal still goes into a Coal Barrel and Resin still goes into a Resin Barrel normally
    through all three paths.
11. If you have another mod that can insert items directly into a container's inventory bypassing
    the normal UI, try inserting a wrong-type item that way. Confirm the barrel's owner-side sanitation ejects it onto the ground next to the barrel immediately (with the periodic sweep remaining as a backup), and
    that no item is deleted or duplicated in the process.

## 4. Range

12. Set `Feeding > Range` low (e.g. 5) via the server's config for a controlled test, or use the
    default 50m with measured placement. Place a compatible light just inside the boundary and
    confirm it gets topped up on the next scan; place one just outside and confirm it does not.

## 5. Top-up behavior

13. Place a coal light at roughly half fuel (not empty) within range of a stocked Coal Barrel.
    Confirm it is topped up to its own maximum on the next scan, not ignored until empty.
14. Place a fully empty compatible light in range. Confirm it fills to its own maximum.
15. Place a completely full compatible light in range. Confirm the barrel's fuel count does not
    change (nothing consumed for a light that doesn't need it).
16. Confirm a Coal light only ever loses fuel from a Coal Barrel's inventory and a Resin light only
    ever from a Resin Barrel's, never mixed.
17. Place a `Fireplace`-based light from another mod (or a modified vanilla one) that burns vanilla
    Coal or Resin and confirm it is serviced automatically with no code changes needed - this
    confirms there is no hardcoded prefab list.

## 6. Multiple overlapping barrels

18. Place 2-3 Coal Barrels at different distances from one under-fueled coal light, each with only
    partial fuel (not enough in any single barrel to fully top up the light). Confirm the nearest
    barrel is drained first and the shortfall is pulled from the next-nearest barrel(s), stopping as
    soon as the light is full or all in-range barrels are empty.
19. Repeat with barrels owned by different connected players (i.e. different ZNetView owners) to
    confirm cross-peer requests resolve correctly and the light still ends up at the right total.

## 7. Concurrency and ownership edge cases

20. Have two players simultaneously open/interact with the same barrel while a scheduled refill
    scan is also drawing from it. Confirm inventory contents stay consistent (no duplication, no
    fuel count going negative or desyncing between clients).
21. Force an ownership change on a barrel or light mid-transaction (e.g. by having its current owner
    move far enough away, or reconnect) while a refill is in flight. Confirm the transaction either
    completes safely or times out and refunds - check the barrel's fuel count before and after to
    confirm no fuel was created or destroyed.
22. Disconnect a client that owns a fireplace right after it should have requested fuel. Confirm the
    corresponding barrel-side reservation (if any) is refunded automatically after its timeout
    (~30 seconds) rather than the fuel disappearing permanently.
23. Destroy a barrel while it has an outstanding reservation for a pending transaction. Confirm no
    exceptions/log spam occur and no fuel appears to have been fabricated elsewhere.
24. Destroy a fireplace/light after a barrel has already granted it fuel but before it can apply
    that fuel. Confirm the granted amount is refunded to the barrel rather than lost.

## 8. Persistence

25. Save and restart the server (or `world save` + reload). Confirm both barrels' inventory
    contents and placement survive intact.

## 9. Duplication / loss audit

26. Across the tests above, tally total Coal and Resin you deposited into barrels versus what ends
    up in barrels + lights + any ejected/dropped stacks. The totals must match - no net creation or
    loss anywhere in the flow.

## 10. Server-authoritative config

27. Set different `Feeding > Range` and `RefillIntervalMinutes` values in a client's local
    `r4v9n1.lightmyfire.cfg` than the server's. Confirm the client's actual behavior follows the
    **server's** values (e.g. by observing the effective range/interval in play), not its own local
    file - this confirms Jotunn's config sync is actually taking effect for these keys.

## 11. Third-party inventory interactions

28. With InventoryLink 0.2.11+ installed, confirm linked crafting never counts or pulls materials
    from either LightMyFire barrel.

---

### Known risks to watch for specifically (see README build note)

- Any `[LightMyFire] Could not patch ...` warning at startup for the `Inventory.AddItem` /
  `Inventory.CanAddItem` overloads means section 3 above is *not* actually enforced for that code
  path - re-check the corresponding method signature in `assembly_valheim.dll` if this appears.
- If item 19 (cross-peer barrel ownership) fails specifically while item 18 (same-owner barrels)
  passes, the issue is most likely in the `ZRoutedRpc`-based grant routing in
  `LightMyFireFeeder.SendGrant`/`RPC_GrantFuel` rather than in the barrel or fireplace logic itself.

## Dotted feeding-radius ring

- Select either LightMyFire barrel in the Hammer menu. Before placement, confirm a thin white dotted circumference is visible around the placement ghost, with the dotted line itself exactly at the configured feeding radius (50 m by default). Confirm there is no filled area/disc.
- Place the barrel. Confirm the circle disappears during normal play.
- Open that barrel's inventory. Confirm the same circle appears around the placed barrel; close the inventory and confirm it disappears again.
- Change the server-synced Feeding > Range value and confirm the circle follows the effective range after config sync.
- With two clients connected, opening a barrel on client A should not make the circle appear on client B.

## 0.5.1 range-ring regression test

- Select either LightMyFire barrel with the hammer: a thin white dotted circumference should be visible at the configured feed radius (50 m by default).
- Move the placement ghost over uneven terrain: the dots should follow nearby ground/surface height instead of remaining on one flat plane.
- Place/open the barrel: the same ring should appear while the container is in use and disappear when closed.
