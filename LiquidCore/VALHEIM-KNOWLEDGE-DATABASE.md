# LiquidCore Valheim knowledge database

`knowledge/valheim-knowledge-v1.json` is the versioned machine-readable contract shared by the Valheim adapter, PCE, and LiquidCore geometry path. It is embedded into `LiquidCore.dll`; startup rejects it safely when the schema, installed `assembly_valheim.dll`, or relevant mod-set fingerprint differs.

The current inventory is keyed to Steam build `21981559`, assembly SHA-256 `3b26c8512778f6e0664b5af2a26f3c30993a00f584c1e76d9123a742b67e2004`, 809 assembly classes, and the recorded eight-plugin mod set (`95daa4f9c5988f4eb0ccb9dd82c2399d03b5df0fdfcdec99582c2b2454f1a081`). It contains:

- 10 geometry-relevant Valheim type rules;
- 12 authoritative or consistency signal contracts;
- 876 fingerprinted network-solid prefab descriptors, including collider types/counts, local bounds, geometry signatures, component/state flags, authoritative callbacks, and PCE/LC handling rules;
- 802 immutable/intact-state geometry descriptors that can bypass collider/component hierarchy reinspection;
- 11 assembly type contracts and 15 callback contracts verified directly against the fingerprinted installed assembly.

The embedded prefab inventory is produced by the opt-in `Update-ValheimKnowledgeAssets.ps1` workflow from a fingerprint-matched Valheim resource census. The expensive census is disabled during normal startup. Unknown assets remain on the existing safe runtime-inspection path once; completed facts are retained in `BepInEx/config/LiquidCore/valheim-knowledge-learned-v1.json` and reused only for the same schema, Valheim assembly, and mod-set fingerprints.

## Cache contract

- Asset cache: immutable or rarely changing prefab/type knowledge.
- PCE instance cache: `sourceId + assetId + transform + state + revision + bounds + tile ownership`.
- LC prepared cache: `sourceId + authoritative revision` owns prepared occupancy, SDF, cut-cell, aperture, and GPU data.
- An unchanged source revision is reused. A changed revision invalidates only dependent data. Removal deletes only that source contribution.

## Current terrain nerve path

For a local terrain operation, `TerrainComp.DoOperation` supplies exact operation bounds before `Heightmap.Regenerate`. The database identifies the resulting signal as `authoritative-local`; the adapter then reuses the cached Heightmap instance, increments its authoritative revision, and publishes only the local bounds. It does not reclassify the hierarchy, enumerate its colliders, hash the full heightfield, or turn the operation back into a discovery sweep.

`Heightmap.Poke -> Heightmap.Regenerate` without causal operation bounds remains a consistency/network fallback because Valheim does not provide an exact local operation contract in that case.

## Inspection

```powershell
.\Inspect-ValheimKnowledge.ps1
.\Inspect-ValheimKnowledge.ps1 -Query "What does Valheim do when terrain is hit?"
.\Inspect-ValheimKnowledge.ps1 -Query "Which callback owns physical removal?"
.\Inspect-ValheimKnowledge.ps1 -Query "Rock_4"
```

Run `Test-ValheimKnowledgeDatabase.ps1` to verify the installed game/build/mod fingerprints, embedded resource, signal rules, terrain fast-path ordering, and every recorded assembly type, base type, field, method, and callback against the installed `assembly_valheim.dll` through Mono.Cecil. A recorded Valheim contract that drifts from the fingerprinted assembly now fails certification instead of silently becoming stale knowledge.

On a valid startup LiquidCore emits an explicit `MATCH` certificate containing
the schema, Steam build, installed assembly hash, and mod-set hash. The same
line confirms that known-asset classification bypasses hierarchy/category
reinspection, reusable immutable/intact-state descriptors bypass collider and
component hierarchy reinspection, and the authoritative local terrain rule
bypasses discovery. Stateful MineRock/door geometry and unknown assets retain
the targeted safe inspection fallback.

Streamed-source appearance is delivered directly from `ZNetScene.CreateObject`. Disappearance uses the shared `ZNetView.ResetZDO` lifecycle point while identity and hierarchy data are still intact; Valheim calls it from explicit destruction, ZDO destruction, stream-out removal, and scene shutdown. PCE therefore adds or removes the exact source contribution without enumerating `ZNetScene.m_instances` or waiting for root rediscovery. The consistency scanner remains a safety fallback for callbacks that cannot supply an active source.
