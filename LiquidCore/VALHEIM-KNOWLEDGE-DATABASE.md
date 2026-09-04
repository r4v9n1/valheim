# LiquidCore Valheim knowledge database

`knowledge/valheim-knowledge-v1.json` is the versioned machine-readable contract shared by the Valheim adapter, PCE, and LiquidCore geometry path. It is embedded into `LiquidCore.dll`; startup rejects it safely when the schema, installed `assembly_valheim.dll`, or relevant mod-set fingerprint differs.

The current inventory is keyed to Steam build `21981559`, assembly SHA-256 `3b26c8512778f6e0664b5af2a26f3c30993a00f584c1e76d9123a742b67e2004`, 809 assembly classes, and the recorded nine-plugin mod set. It contains:

- 10 geometry-relevant Valheim type rules;
- 12 authoritative or consistency signal contracts;
- 7 prefabs observed in retained LiquidCore live traces.

The observed-prefab list is deliberately incremental. Unknown assets remain on the existing safe runtime-inspection path and can be learned without invalidating known entries. No broad prefab scan is performed on normal startup.

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

Run `Test-ValheimKnowledgeDatabase.ps1` to verify the installed game/build/mod fingerprints, embedded resource, signal rules, and terrain fast-path ordering.
