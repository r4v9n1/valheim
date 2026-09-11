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

The descriptor validation seam now fails closed when `CompleteSourceDomain` is
asserted without a stable partition identity or complete capacity, sparse
storage, and per-cell membership fields. Active E3 windows remain valid
non-source publications because they continue to set `CompleteSourceDomain=false`.
The authoritative workspace source hash after this guard is
`BEE970E96C442078B6357506DD59B02B096E8F517C69F6988D25A0DEA2ACD7E4`; the
canonical build passed with zero errors and produced DLL hash
`30FB5D178360F1657B325AC01B6436915C923B93EFA42E850669A512CBBC9DC5`.

The authoritative workspace source revision for the guard is Git commit
`55bde11` (`LiquidCore: guard complete PCE source domains`) in
`build/Valheim/workspace/LiquidCore`; it is pushed to that workspace's
`origin/main`. The repository integration/documentation revision is
`eaab861` in `build/Valheim/repos/valheim`.

The LiquidCore source calculator used by the build is additionally
checkpointed in workspace commit `85e4a0e` (`LiquidCore: checkpoint initial
source calculator`), also pushed. The source caller remains intentionally
unwired until a complete global or partitioned domain is available.

Workspace checkpoint `47410b8` adds the geometry-only
`LiquidCoreInitialWorldWaterDomain` manifest. It rejects missing partitions,
duplicate identities, revision mismatches, positive-volume overlap, and
incomplete spatial coverage before delegating to LiquidCore's source-plan
calculator. It performs no water or E3 operation. The canonical build passed
with zero errors and produced DLL hash
`49F9E69FFF6A800841994CD13F6E0970D9E9861DA392013E9D84B3927CEBA481`.

Workspace validation checkpoint `d548e89` adds
`RunLiquidCoreInitialWorldDomainValidation`. Its focused cases accept a fully
covered partition set and reject a gap, positive-volume overlap, and revision
mismatch. It is a structural contract regression only; it does not synthesize
terrain or water and does not authorize the production source caller.

Workspace checkpoint `2a75f5d` fixes the namespace imports and the focused
Unity 6000.0.61f1/D3D11 run passes all four cases (`RESULT: PASS`). The
canonical DLL remains build-clean with hash
`87927CB81000A313C6E5F8A774FB0EF1CB4C3E6557526C2631C5A0B2EFFF1429`.

## Durable checkpoint provenance audit — 2026-09-11

The authoritative UnityPhysicalOcean source is already Git-controlled in
`build/Valheim/workspace/LiquidCore` as the `PhysicalWaterUnity` repository;
it is not copied into `repos/valheim`. Its current durable source checkpoint
is workspace commit `2a75f5d`
(`LiquidCore: pass complete-domain Unity regression`), and that commit is
present on `origin/main` at
`https://github.com/r4v9n1/PhysicalWaterUnity.git`.

The corresponding repository integration/documentation checkpoint is
`dccc801` (`LiquidCore: record focused domain regression pass`) in
`build/Valheim/repos/valheim`, present on its configured `origin/main`.
The verified build product from the workspace source is
`LiquidCore.dll`, SHA-256
`87927CB81000A313C6E5F8A774FB0EF1CB4C3E6557526C2631C5A0B2EFFF1429`.

These three identifiers are the recovery/provenance tuple for this gate:
repository integration commit, exact authoritative workspace source commit,
and built DLL hash. Older dirty workspace files belong to separate
in-progress solver/renderer/persistence work and are intentionally excluded
from this source-domain checkpoint.

## Installed-host domain audit — 2026-09-11

The installed Valheim 1.0 `WorldGenerator` was inspected from the retained
managed assembly. It exposes a finite procedural terrain domain through
`worldSize = 10000f`, `waterEdge = 10500f`, and deterministic
`GetHeight(float,float)`/biome-generation methods. That is terrain geometry
only. The API does not publish a complete ocean connected-component map,
barrier/saddle topology, or a cell storage-capacity publication covering the
world.

Consequently the current PCE publication remains an active/player-centred
window with `CompleteSourceDomain=false`. `WorldGenerator.GetBiome`,
SeaLevel, renderer masks, and the active E3 window are not valid substitutes
for the missing complete finite-water domain. No production
`InitialWorldWaterSource` caller is wired, and no source receipt is created.
This is deliberate fail-closed behavior: the next implementation must add a
complete or explicitly partitioned geometry/capacity publication before any
LiquidCore source atoms are computed or committed.

## Follow-up source checkpoints — 2026-09-11

The focused complete-domain regression now also proves the LiquidCore source
calculator result: the two complete test partitions produce exactly `8 m3`
and `64000` atoms at the declared reference head, while gap, overlap, and
revision-mismatch domains remain rejected. That workspace validation is
checkpointed and pushed as `c09cdf9`.

The previously uncommitted, directly affected runtime changes were then
separated into the pushed workspace checkpoint `7a5598e`
(`LiquidCore: checkpoint owned geometry overflow and receipts`). It contains
the owned post-geometry vertical overflow queue, persistence fields, and the
validated redistribution allocator/validation changes. The intended cel
surface correction is separately checkpointed and pushed as `7d35965`
(`LiquidCore: enforce cel-shaded ocean surface path`).

The canonical DLL was rebuilt against workspace `7d35965` with zero errors
and ten existing warnings. Its SHA-256 is
`BA7C8E49C6F9152139FAB61D64614BE0C7C40A7776C66F5B9B6C605D3F13117A`.
The rebuilt AssetBundle passed shader/kernel/material validation and has
SHA-256
`CAB7E46490284C2D75AF2EF27CE419888BB93DD570B94E5B7EFE7EC8626859F3`.

The post-checkpoint focused regressions passed for redistribution, persistence
and authoritative water queries. Persistence explicitly reports vertical
overflow save/restore (`123456789` atoms), and the source-domain regression
reports the exact `8 m3`/`64000`-atom plan.

The retained terrain witness at
`live-failures/20260905-water-product-rejection/20260905-002830` was used;
it is technically suitable and reached
`TERRAIN COMPOSITE DISPLACEMENT: PASS`. Its full replay did not produce a
terminal report: GPU readback reached approximately `219 s` for the
`lower:s300` phase and then stalled. This is incomplete validation, not a
PASS and not evidence that the terrain gate is complete. No new terrain data
was synthesized.

The bounded 60-step retained-terrain witness was attempted on 2026-09-11
using the same retained snapshot. It reached `raise:s60` with
`acceptedRoots=240/240`, `attemptedRoots=240`, and reported
`COMPOSITE CARRY-ONLY PUBLISHED HEADS: PASS` (`carryOnly=39`, `hidden=0`).
The run then stalled during the post-settle diagnostic capture before
emitting its terminal report; its last measured terrain-step readback was
`44568.994 ms`. Therefore the bounded witness result is **INCOMPLETE**, not
PASS or FAIL. The 1800-step replay remains an outstanding expensive
regression and is not promoted to the primary task unless it exposes a
regression caused by the source-domain work.

## Global capacity-provider audit — 2026-09-11

The current production path was traced without changing source. E1 queues
CODY coverage from `_geometryCoverageBounds`, which is derived from the
player-centred E3 window. `LiquidCorePceRuntime` enumerates only 24 m logical
regions intersecting that represented window, builds CODY snapshots from the
active cut-face arrays, and `PublishAppliedDomainCapacityStorage` copies the
same active-domain capacity/storage arrays. No code enumerates the complete
Valheim world, proves a connected-ocean component across all partitions, or
retains complete dormant capacity outside the active E3 window.

Therefore the existing `VolumetricPceCapacityStorageDescriptor` remains a
geometry-only active-window publication. It cannot be promoted to
`CompleteSourceDomain=true`, and the existing `LiquidCoreInitialWorldWaterDomain`
aggregator must not be fed these windows as a whole-world source. Doing so
would make a local grid define the ocean and would violate the no-overlap,
complete-domain, and exactly-once source accounting invariants.

No production source caller was wired and no source atoms were created. The
required next implementation is an actual CODY/PCE global or explicitly
enumerated non-overlapping partition provider that publishes complete storage
curves and membership for every source partition, with a stable revision and
aggregate coverage proof. Until that provider exists, the current fail-closed
behavior is preserved.

The complete-domain membership guard was strengthened in workspace checkpoint
`c0e8233` and pushed to the authoritative `PhysicalWaterUnity` remote. A
focused Unity regression now rejects an open complete-domain cell with zero
catchment identity and reports `MEMBERSHIP: PASS`; the complete fixture still
reports the exact `8 m3` / `64000`-atom plan. The canonical DLL rebuilt with
zero errors and ten existing warnings; its SHA-256 is
`C7326860D21366682EC67A39B756FF6560DA0A5CEE3A0299A880410EA3363010`.
This validates the contract guard only; it does not promote the active E3
window into a real global source domain.

The PCE integration boundary now accepts a validated, deep-copied
`LiquidCoreInitialWorldWaterDomain` publication with stable identity and
monotonic geometry revision. Workspace checkpoint `63825a3` contains the
geometry-only clone/validation support. The production caller still has no
complete domain to publish, so no source transaction or water ledger mutation
is performed. The DLL built from this checkpoint has SHA-256
`B06D30915066F7F962EF2AB51D0076BFCFE31E354B27378561592CDF339BC4CF`.
