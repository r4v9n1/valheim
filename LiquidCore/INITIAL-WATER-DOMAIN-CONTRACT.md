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

## Completeness audit — 2026-09-11

The current producer is `LiquidCorePceRuntime.PublishAppliedDomainCapacityStorage`.
It receives the applied `VolumetricWaterDomain`, so its exact spatial domain is:

`WorldOrigin .. WorldOrigin + (ResolutionX, ResolutionY, ResolutionZ) * CellSize`

The production E3 domain is a rebased/player-centred streaming window. Its
configured settings are finite local resolution and cell size, and the domain
origin changes when the active window recenters. This proves it is not the
complete initial WaterBody and not a stable globally identified partition.

The current CODY publication does not repair that limitation. `CodyCatchmentDescriptor`
contains a 24 m region bucket, dependency bounds, revision metadata, and
drainage-exit topology. `CapturePersistentDescriptors()` enumerates those
knowledge descriptors, but no complete world storage curves or per-cell
capacity ledger are published outside the active E3 domain. CODY catchment
identity and dependency bounds therefore cannot be used as proof of global
source completeness.

The active publication deliberately sets `CompleteSourceDomain=false` and
uses a deterministic `active-window:` partition identity. The LiquidCore
calculator rejects it. No source transaction, single global receipt, or E3
materialization is wired from this path. The validated `CatchmentId = 0`
assignment for other local components remains fail-closed; it is not an
assertion that those components are absent from the eventual ocean.

The next implementation must publish either one complete dormant/global
capacity representation or a set of complete, stable, non-overlapping source
partitions. Each partition must carry its own geometry/dependency revisions,
membership, exact capacity representation, and idempotency identity before
LiquidCore computes `V_source` and exact atoms. Activating a committed
partition must then materialize existing owned water and add zero source
volume.
