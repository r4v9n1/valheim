# LiquidCore initial-water domain contract

The applied `VolumetricPceCapacityStorageDescriptor` currently covers only
the active E3 window:

`WorldOrigin .. WorldOrigin + (ResolutionX, ResolutionY, ResolutionZ) * CellSize`

That is a player-centred streaming representation. It is not the complete
initial ocean and is not a globally complete source partition. CODY catchment
identity and dependency bounds do not establish a global finite storage
ledger.

Active-window publications therefore carry a deterministic
`SourcePartitionId` but set `CompleteSourceDomain=false`. LiquidCore rejects
such descriptors for initial source calculation.

The aggregate source plan accepts only complete, non-overlapping PCE
partitions. LiquidCore computes each partition's exact volume and atom
transaction, then proves the aggregate totals:

`V_initial = sum(V_partition)`

`N_initial = sum(N_partition)`

E3 receipt/materialization is intentionally not connected to this plan yet.
Activation of a committed partition must later be a representation transfer,
not a new source operation.
