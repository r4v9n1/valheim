using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using R4V9N1.PhysicalOcean.Cody;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class SourceRuntimeRecord
    {
        internal string SourceId;
        internal string AssetClassId;
        internal uint AuthoritativeRevision;
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal Vector3 Scale;
        internal Bounds WorldBounds;
        internal Vector3Int PceTileMin;
        internal Vector3Int PceTileMax;
        internal int PceOccupancyHandle;
        internal int LcPreparedGeometryHandle;
        internal VolumetricPreparedGeometryDescriptor PreparedGeometry;
        internal Bounds SdfDependencyRegion;
        internal Bounds CutCellDependencyRegion;
        internal Bounds ApertureDependencyRegion;
        internal Bounds GpuResidentRegion;
        internal int GpuResidentRegionHandle;
        internal int SdfCellsUpdated;
        internal int CutCellsUpdated;
        internal int ApertureFacesUpdated;
        internal int GpuBytesPatched;
        internal uint LastAppliedLcRevision;
        internal long LastAppliedGeneration;
        internal bool DatabaseHit;
    }

    internal sealed class BaseWorldPceBootstrapJob
    {
        internal string WorldKey;
        internal string DomainId;
        internal ulong SourceCatchmentId;
        internal Bounds SourceBounds;
        internal Bounds[] PartitionBounds;
        internal Vector2 PartitionSize;
        internal float CellSize;
        internal long GeometryRevision;
        internal string DependencyRevisionHash;
        internal long EstimatedCompactGeometryBytes;
        // Provisional geometry is captured once per deterministic partition.
        // Closure must consume that exact snapshot rather than resampling the
        // entire world after the incremental bootstrap budget is exhausted.
        internal readonly List<VolumetricPceCapacityStorageDescriptor> ProvisionalPartitions =
            new List<VolumetricPceCapacityStorageDescriptor>();
        internal int NextPartition;
    }

    internal sealed class BaseWorldPceClosureResult
    {
        internal BaseWorldPceBootstrapJob Job;
        internal bool Valid;
        internal VolumetricPceCapacityStorageDescriptor[] Closed;
        internal string Error;
        internal double ElapsedMilliseconds;
        internal long ManagedBefore;
        internal long ManagedAfter;
    }

    internal sealed class LiquidCorePceRuntime : MonoBehaviour
    {
        private readonly ProbeColonyWorld _world = new ProbeColonyWorld();
        private ProbeColonyGeometryBridge _geometry;
        private ProbeColonyEventBridge _events;
        private PhysicalWaterValheimWorldGeometryAdapter _adapter;
        private ProbeColonyChangeQueue _queue;
        private readonly ProbeColonyCausalGeometrySignalQueue _causalSignals = new ProbeColonyCausalGeometrySignalQueue();
        private readonly Dictionary<string, ValheimPceGeometryChange> _activeSources = new Dictionary<string, ValheimPceGeometryChange>();
        private readonly Dictionary<string, SourceRuntimeRecord> _sourceRuntimeRecords = new Dictionary<string, SourceRuntimeRecord>();
        private readonly Dictionary<string, VolumetricPreparedGeometryDescriptor> _preparedGeometryByAsset =
            new Dictionary<string, VolumetricPreparedGeometryDescriptor>(StringComparer.Ordinal);
        // A connected catchment can span multiple non-overlapping source
        // partitions. SourcePartitionId is therefore the storage identity;
        // CatchmentId remains geometry/topology metadata, not a partition key.
        private readonly Dictionary<string, VolumetricPceCapacityStorageDescriptor> _capacityStorageByPartition =
            new Dictionary<string, VolumetricPceCapacityStorageDescriptor>(StringComparer.Ordinal);
        private LiquidCoreInitialWorldWaterDomain _completeInitialWorldDomain;
        // The Valheim adapter publishes a root-level digest for the exact
        // ready snapshot. Retain that digest at the PCE boundary; individual
        // event revisions are source-level and cannot represent overlapping
        // sources under one Unity root on their own.
        private readonly Dictionary<int, int> _causalRootRevisionDigests = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _snapshotAdapterRevisions = new Dictionary<int, int>();
        private readonly HashSet<int> _snapshotAdapterRootIds = new HashSet<int>();
        private readonly HashSet<int> _snapshotRepresentedRootIds = new HashSet<int>();
        private readonly Dictionary<int, GameObject> _snapshotPceRoots = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, int> _snapshotPceRevisions = new Dictionary<int, int>();
        private readonly HashSet<int> _preparedDescriptorAmbiguousRoots = new HashSet<int>();
        private readonly List<Collider> _mappedPreparedColliders = new List<Collider>();
        private readonly List<Collider> _mappedColliderComponentScratch = new List<Collider>();
        private int _publishedEvents;
        private int _nextSourceRuntimeHandle;
        private int _directQueueBypasses;
        private const float CodyRegionWorldSize = 24f;
        // Catchment descriptors have their own semantic schema. Revision 2
        // requires every represented local/open-region exit to carry the
        // bounded sub-grid-candidate hint consumed by MVC after physical-path
        // proof fails. Do not couple this to the Valheim knowledge JSON schema.
        private const int CodyCatchmentSchemaVersion = 3;
        private CodyCatchmentCache _codyL1;
        private CodyCatchmentCache _codyL2;
        private CodyCatchmentRebuildCoordinator _codyRebuilds;
        private string _codyL2Path;
        private string _codyInitialWorldSourceRelationPath;
        private bool _codyPersistenceWritable;
        private bool _codyL2ArtifactRejected;
        private bool _hasCodyWarmBounds;
        private Bounds _codyWarmBounds;
        private readonly List<ulong> _codyInvalidated = new List<ulong>();
        private VolumetricFiniteDomainController _pendingCodyDomain;
        private Bounds _pendingCodyCoverageBounds;
        private long _pendingCodyGeometryRevision = -1;
        private string _baseWorldBootstrapWorldKey;
        private string _baseWorldBootstrapDomainFingerprint;
        private string _baseWorldBootstrapAttemptKey;
        private BaseWorldPceBootstrapJob _baseWorldPceBootstrapJob;
        private Task<BaseWorldPceClosureResult> _baseWorldPceClosureTask;
        private bool _baseWorldBootstrapFailureReported;
        private string _baseWorldBootstrapGateState;
        private bool _configuredSourceMarkerApplied;
        private bool _configuredSourceMarkerReported;
        internal static LiquidCorePceRuntime Instance { get; private set; }

        internal ProbeColonyWorld World => _world;
        internal CodyCatchmentCache CodyL1 => _codyL1;
        internal event Action<CodyCatchmentDescriptor> CodyCatchmentPublished;
        internal event Action<VolumetricPceCapacityStorageDescriptor> CapacityStoragePublished;
        internal event Action<LiquidCoreInitialWorldWaterDomain> CompleteInitialWorldDomainPublished;

        internal bool TryGetCompleteInitialWorldDomain(
            out LiquidCoreInitialWorldWaterDomain domain)
        {
            domain = _completeInitialWorldDomain?.Clone();
            return domain != null;
        }

        internal bool PublishCompleteInitialWorldDomain(
            LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            error = string.Empty;
            if (domain == null || !domain.ValidateComplete(out error)) return false;
            if (_completeInitialWorldDomain != null)
            {
                if (!string.Equals(_completeInitialWorldDomain.DomainId, domain.DomainId,
                        StringComparison.Ordinal))
                {
                    error = "PCE complete source publication changed the stable domain identity.";
                    return false;
                }
                if (domain.GeometryRevision < _completeInitialWorldDomain.GeometryRevision)
                {
                    error = "PCE complete source publication is older than the retained geometry revision.";
                    return false;
                }
            }
            _completeInitialWorldDomain = domain.Clone();
            CompleteInitialWorldDomainPublished?.Invoke(_completeInitialWorldDomain.Clone());
            return true;
        }

        private bool PublishCompleteInitialWorldDomainOwned(
            LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            error = string.Empty;
            if (domain == null || !domain.ValidateComplete(out error)) return false;
            if (_completeInitialWorldDomain != null)
            {
                if (!string.Equals(_completeInitialWorldDomain.DomainId, domain.DomainId,
                        StringComparison.Ordinal))
                {
                    error = "PCE complete source publication changed the stable domain identity.";
                    return false;
                }
                if (domain.GeometryRevision < _completeInitialWorldDomain.GeometryRevision)
                {
                    error = "PCE complete source publication is older than the retained geometry revision.";
                    return false;
                }
            }
            // Ownership is transferred from the provider-owned closed
            // descriptor set. Subscribers receive an isolated clone.
            _completeInitialWorldDomain = domain;
            CompleteInitialWorldDomainPublished?.Invoke(_completeInitialWorldDomain.Clone());
            return true;
        }

        internal bool TryGetCapacityStorage(ulong catchmentId,
            out VolumetricPceCapacityStorageDescriptor descriptor)
        {
            foreach (VolumetricPceCapacityStorageDescriptor candidate in _capacityStorageByPartition.Values)
            {
                if (candidate.CatchmentId != catchmentId) continue;
                descriptor = candidate;
                return true;
            }
            descriptor = null;
            return false;
        }

        internal bool TryGetCapacityStorage(string sourcePartitionId,
            out VolumetricPceCapacityStorageDescriptor descriptor)
        {
            if (string.IsNullOrWhiteSpace(sourcePartitionId))
            {
                descriptor = null;
                return false;
            }
            return _capacityStorageByPartition.TryGetValue(sourcePartitionId, out descriptor);
        }

        internal bool TryResolveCodySourceCatchment(
            Vector3 sourceSeedPosition, out ulong catchmentId, out string error)
        {
            catchmentId = 0UL;
            error = string.Empty;
            if (!Finite(sourceSeedPosition) || _codyL1 == null ||
                !_codyL1.TryLookup(sourceSeedPosition, out CodyCatchmentDescriptor descriptor))
            {
                error = "No validated CODY catchment covers the explicit initial-water source seed.";
                return false;
            }
            if (descriptor.CatchmentId == 0UL)
            {
                error = "The CODY source seed resolved to an invalid catchment identity.";
                return false;
            }
            catchmentId = descriptor.CatchmentId;
            return true;
        }

        internal bool TryResolveCodyInitialWaterSourceCatchment(
            out ulong catchmentId, out string error)
        {
            catchmentId = 0UL;
            if (!TryGetCodyInitialWaterSourceSeed(out CodyCatchmentDescriptor descriptor, out error)) return false;
            catchmentId = descriptor.CatchmentId;
            return catchmentId != 0UL;
        }

        private bool TryGetCodyInitialWaterSourceSeed(
            out CodyCatchmentDescriptor descriptor, out string error)
        {
            descriptor = null;
            error = string.Empty;
            if ((_codyL1 == null || !_codyL1.TryGetInitialWaterSourceSeed(out descriptor)) &&
                (_codyL2 == null || !_codyL2.TryGetInitialWaterSourceSeed(out descriptor)))
            {
                error = "CODY has not published a validated initial-water source seed.";
                return false;
            }
            if (descriptor == null || descriptor.CatchmentId == 0UL)
            {
                descriptor = null;
                error = "CODY initial-water source seed has an invalid catchment identity.";
                return false;
            }
            return true;
        }

        internal bool TryBuildCompleteInitialWorldDomain(
            string domainId, Bounds sourceBounds, long geometryRevision,
            string dependencyRevisionHash,
            out LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            var partitions = new List<VolumetricPceCapacityStorageDescriptor>(
                _capacityStorageByPartition.Values);
            return LiquidCoreInitialWorldWaterDomain.TryAssembleComplete(
                domainId, sourceBounds, geometryRevision, dependencyRevisionHash,
                partitions, out domain, out error);
        }

        internal bool TryEnumerateBaseWorldPcePartitions(
            float verticalMin, float verticalMax, Vector2 partitionSize,
            out Bounds sourceBounds, out Bounds[] partitions, out string error)
        {
            sourceBounds = default(Bounds);
            partitions = Array.Empty<Bounds>();
            if (!LiquidCoreValheimBaseWorldDomain.TryGetBounds(
                    verticalMin, verticalMax, out sourceBounds, out error)) return false;
            return VolumetricPceSourcePartitionGrid.TryBuild(
                sourceBounds, partitionSize, out partitions, out error);
        }

        internal bool TryBuildBaseTerrainPcePartition(
            Bounds partitionBounds, Vector2 partitionSize, float cellSize,
            long geometryRevision, string dependencyRevisionHash,
            out VolumetricPceCapacityStorageDescriptor descriptor, out string error)
        {
            return LiquidCoreValheimBaseTerrainPceBuilder.TryBuildFromWorldGenerator(
                partitionBounds, partitionSize, cellSize, geometryRevision,
                dependencyRevisionHash, out descriptor, out error);
        }

        internal bool TryBuildBaseWorldPcePartitions(
            float verticalMin, float verticalMax, Vector2 partitionSize, float cellSize,
            long geometryRevision, string dependencyRevisionHash,
            out Bounds sourceBounds,
            out VolumetricPceCapacityStorageDescriptor[] partitions,
            out string error)
        {
            sourceBounds = default(Bounds);
            partitions = Array.Empty<VolumetricPceCapacityStorageDescriptor>();
            if (!TryEnumerateBaseWorldPcePartitions(
                    verticalMin, verticalMax, partitionSize,
                    out sourceBounds, out Bounds[] bounds, out error)) return false;

            var result = new VolumetricPceCapacityStorageDescriptor[bounds.Length];
            for (int i = 0; i < bounds.Length; i++)
            {
                if (!TryBuildBaseTerrainPcePartition(
                        bounds[i], partitionSize, cellSize, geometryRevision,
                        dependencyRevisionHash, out result[i], out error))
                {
                    partitions = Array.Empty<VolumetricPceCapacityStorageDescriptor>();
                    return false;
                }
            }
            partitions = result;
            return true;
        }

        internal bool TryCloseBaseWorldPcePartitions(
            Bounds sourceBounds, Vector2 partitionSize,
            long geometryRevision, string dependencyRevisionHash,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> provisional,
            out VolumetricPceCapacityStorageDescriptor[] closed, out string error)
        {
            return VolumetricPceGlobalConnectivityClosure.TryClose(
                sourceBounds, partitionSize, geometryRevision, dependencyRevisionHash,
                provisional, out closed, out error);
        }

        private static bool TryCloseBaseWorldPcePartitionsOwned(
            Bounds sourceBounds, Vector2 partitionSize,
            long geometryRevision, string dependencyRevisionHash,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> provisional,
            out VolumetricPceCapacityStorageDescriptor[] closed, out string error)
        {
            return VolumetricPceGlobalConnectivityClosure.TryCloseOwned(
                sourceBounds, partitionSize, geometryRevision, dependencyRevisionHash,
                provisional, out closed, out error);
        }

        internal bool TryPublishBaseWorldPceDomain(
            string domainId, float verticalMin, float verticalMax,
            Vector2 partitionSize, float cellSize, long geometryRevision,
            string dependencyRevisionHash,
            out LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            domain = null;
            if (!TryBuildBaseWorldPcePartitions(
                    verticalMin, verticalMax, partitionSize, cellSize,
                    geometryRevision, dependencyRevisionHash,
                    out Bounds sourceBounds,
                    out VolumetricPceCapacityStorageDescriptor[] provisional,
                    out error)) return false;
            if (!TryCloseBaseWorldPcePartitions(
                    sourceBounds, partitionSize, geometryRevision,
                    dependencyRevisionHash, provisional,
                    out VolumetricPceCapacityStorageDescriptor[] closed,
                    out error)) return false;
            if (!VolumetricPceCompletePartitionAssembler.TryAssemble(
                domainId, sourceBounds, partitionSize, geometryRevision,
                dependencyRevisionHash, closed, out domain, out error))
            {
                domain = null;
                return false;
            }
            if (!CanPublishCompleteBaseWorldPceDomain(domain, closed, out error))
            {
                domain = null;
                return false;
            }
            for (int i = 0; i < closed.Length; i++)
                if (!PublishCapacityStorage(closed[i], out error))
                {
                    domain = null;
                    return false;
                }
            return PublishCompleteInitialWorldDomain(domain, out error);
        }

        internal bool TryPublishBaseWorldPceDomain(
            string domainId, Vector3 sourceSeedPosition,
            float verticalMin, float verticalMax, Vector2 partitionSize,
            float cellSize, long geometryRevision, string dependencyRevisionHash,
            out LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            domain = null;
            if (!TryResolveCodySourceCatchment(sourceSeedPosition, out ulong sourceCatchmentId, out error))
                return false;
            return TryPublishBaseWorldPceDomainForCatchment(
                domainId, sourceCatchmentId, verticalMin, verticalMax, partitionSize,
                cellSize, geometryRevision, dependencyRevisionHash, out domain, out error);
        }

        // The production bootstrap consumes an explicit CODY-selected
        // catchment identity. It must not select a source from the player,
        // biome, SeaLevel, renderer coverage, or the active E3 window.
        internal bool TryPublishBaseWorldPceDomainForCatchment(
            string domainId, ulong sourceCatchmentId,
            float verticalMin, float verticalMax, Vector2 partitionSize,
            float cellSize, long geometryRevision, string dependencyRevisionHash,
            out LiquidCoreInitialWorldWaterDomain domain, out string error)
        {
            domain = null;
            if (sourceCatchmentId == 0UL)
            {
                error = "Complete base-world PCE publication requires an explicit non-zero CODY catchment identity.";
                return false;
            }
            if (!TryBuildBaseWorldPcePartitions(
                    verticalMin, verticalMax, partitionSize, cellSize,
                    geometryRevision, dependencyRevisionHash,
                    out Bounds sourceBounds,
                    out VolumetricPceCapacityStorageDescriptor[] provisional,
                    out error)) return false;
            if (!TryCloseBaseWorldPcePartitions(
                    sourceBounds, partitionSize, geometryRevision,
                    dependencyRevisionHash, provisional,
                    out VolumetricPceCapacityStorageDescriptor[] closed,
                    out error)) return false;
            if (!TryGetCodyInitialWaterSourceSeed(
                    out CodyCatchmentDescriptor sourceSeed, out error) ||
                sourceSeed.CatchmentId != sourceCatchmentId ||
                !TryResolveClosedPceCatchment(sourceSeed, closed,
                    out ulong globalSourceCatchmentId, out error)) return false;
            if (!VolumetricPceCompletePartitionAssembler.TryAssemble(
                domainId, sourceBounds, partitionSize, geometryRevision,
                dependencyRevisionHash, closed, out domain, out error))
            {
                domain = null;
                return false;
            }
            domain.SourceCatchmentId = globalSourceCatchmentId;
            if (!CanPublishCompleteBaseWorldPceDomain(domain, closed, out error))
            {
                domain = null;
                return false;
            }
            for (int i = 0; i < closed.Length; i++)
                if (!PublishCapacityStorage(closed[i], out error))
                {
                    domain = null;
                    return false;
                }
            return PublishCompleteInitialWorldDomain(domain, out error);
        }

        private bool CanPublishCompleteBaseWorldPceDomain(
            LiquidCoreInitialWorldWaterDomain domain,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> closed,
            out string error)
        {
            error = string.Empty;
            if (domain == null || closed == null || closed.Count == 0)
            {
                error = "Complete base-world PCE publication preflight received no assembled domain.";
                return false;
            }
            if (_completeInitialWorldDomain != null)
            {
                if (!string.Equals(_completeInitialWorldDomain.DomainId, domain.DomainId,
                        StringComparison.Ordinal))
                {
                    error = "PCE complete source publication changed the stable domain identity.";
                    return false;
                }
                if (domain.GeometryRevision < _completeInitialWorldDomain.GeometryRevision)
                {
                    error = "PCE complete source publication is older than the retained geometry revision.";
                    return false;
                }
            }
            for (int i = 0; i < closed.Count; i++)
            {
                VolumetricPceCapacityStorageDescriptor candidate = closed[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.SourcePartitionId))
                {
                    error = "PCE complete source publication contains an invalid partition identity.";
                    return false;
                }
                if (_capacityStorageByPartition.TryGetValue(
                        candidate.SourcePartitionId, out VolumetricPceCapacityStorageDescriptor previous) &&
                    candidate.GeometryRevision < previous.GeometryRevision)
                {
                    error = "PCE complete source publication is older than a retained partition revision.";
                    return false;
                }
            }
            return true;
        }

        private bool TryResolvePersistedSourceRelation(
            string worldKey, long geometryRevision, string dependencyRevisionHash,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> closed,
            out ulong[] sourceCatchmentIds, out string error)
        {
            sourceCatchmentIds = Array.Empty<ulong>();
            error = string.Empty;
            CodyInitialWorldSourceRelation relation = _codyL2?.InitialWorldSourceRelation ??
                _codyL1?.InitialWorldSourceRelation;
            if (relation == null)
            {
                error = "No persisted CODY initial-world source relation.";
                return false;
            }
            if (!relation.Validate(out error) ||
                !string.Equals(relation.WorldKey, worldKey, StringComparison.Ordinal) ||
                relation.GeometryRevision != geometryRevision ||
                !string.Equals(relation.DependencyRevisionHash, dependencyRevisionHash, StringComparison.Ordinal))
            {
                if (string.IsNullOrEmpty(error)) error = "Persisted CODY source relation is stale for the closed world domain.";
                return false;
            }
            var present = new HashSet<ulong>();
            for (int i = 0; i < closed.Count; i++)
            {
                ulong[] membership = closed[i]?.CellCatchmentIds;
                if (membership == null) continue;
                for (int j = 0; j < membership.Length; j++) if (membership[j] != 0UL) present.Add(membership[j]);
            }
            for (int i = 0; i < relation.SourceCatchmentIds.Length; i++)
                if (!present.Contains(relation.SourceCatchmentIds[i]))
                {
                    error = "Persisted CODY source relation names a catchment absent from final PCE closure.";
                    return false;
                }
            sourceCatchmentIds = (ulong[])relation.SourceCatchmentIds.Clone();
            return true;
        }

        private void PersistResolvedSourceRelation(
            BaseWorldPceBootstrapJob job, ulong[] sourceCatchmentIds)
        {
            if (job == null || sourceCatchmentIds == null || sourceCatchmentIds.Length == 0 ||
                _codyL2 == null || string.IsNullOrEmpty(_codyInitialWorldSourceRelationPath)) return;
            var relation = new CodyInitialWorldSourceRelation
            {
                WorldKey = job.WorldKey,
                GeometryRevision = job.GeometryRevision,
                DependencyRevisionHash = job.DependencyRevisionHash,
                RelationRevision = job.GeometryRevision,
                SourceCatchmentIds = (ulong[])sourceCatchmentIds.Clone()
            };
            if (!_codyL2.TryPublishInitialWorldSourceRelation(relation, out string publishError))
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore could not retain the resolved CODY initial-world source relation: " + publishError + ".");
                return;
            }
            try
            {
                CodyInitialWorldSourceRelationStore.Save(_codyInitialWorldSourceRelationPath, relation);
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore persisted the resolved CODY initial-world source relation: catchments=" +
                    string.Join(",", Array.ConvertAll(sourceCatchmentIds, id => id.ToString())) + ".");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore could not persist the resolved CODY initial-world source relation: " + ex.Message + ".");
            }
        }

        private static bool TryResolveClosedPceInitialSourceSet(
            Bounds sourceBounds,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> closed,
            out ulong[] sourceCatchmentIds, out string error)
        {
            sourceCatchmentIds = Array.Empty<ulong>();
            error = string.Empty;
            if (PhysicalWaterPlugin.Settings == null)
            {
                error = "Initial-world source settings are unavailable.";
                return false;
            }
            return VolumetricPceInitialSourceRelation.TryResolve(
                sourceBounds, closed, PhysicalWaterPlugin.Settings.SeaLevel.Value,
                out sourceCatchmentIds, out error);
        }

        private static bool TryResolveClosedPceCatchment(
            CodyCatchmentDescriptor sourceSeed,
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> closed,
            out ulong globalCatchmentId, out string error)
        {
            globalCatchmentId = 0UL;
            error = string.Empty;
            if (sourceSeed == null || closed == null || closed.Count == 0 ||
                !FiniteBounds(sourceSeed.DependencyBounds))
            {
                error = "CODY source seed does not provide a finite spatial selection region.";
                return false;
            }
            var candidates = new HashSet<ulong>();
            for (int p = 0; p < closed.Count; p++)
            {
                VolumetricPceCapacityStorageDescriptor descriptor = closed[p];
                if (descriptor == null || !descriptor.WorldBounds.Intersects(sourceSeed.DependencyBounds)) continue;
                int cellCount = descriptor.ResolutionX * descriptor.ResolutionY * descriptor.ResolutionZ;
                for (int cell = 0; cell < cellCount; cell++)
                {
                    if (descriptor.CellComponentIds[cell] < 0 || descriptor.CellCatchmentIds[cell] == 0UL) continue;
                    int x = cell % descriptor.ResolutionX;
                    int y = cell / descriptor.ResolutionX % descriptor.ResolutionY;
                    int z = cell / (descriptor.ResolutionX * descriptor.ResolutionY);
                    Bounds cellBounds = new Bounds(
                        descriptor.GridWorldOrigin + new Vector3(
                            (x + 0.5f) * descriptor.CellSize,
                            (y + 0.5f) * descriptor.CellSize,
                            (z + 0.5f) * descriptor.CellSize),
                        Vector3.one * descriptor.CellSize);
                    if (cellBounds.Intersects(sourceSeed.DependencyBounds))
                        candidates.Add(descriptor.CellCatchmentIds[cell]);
                }
            }
            if (candidates.Count != 1)
            {
                error = candidates.Count == 0
                    ? "CODY source seed does not map to an open post-closure PCE component."
                    : "CODY source seed maps to multiple post-closure PCE components.";
                return false;
            }
            foreach (ulong candidate in candidates) globalCatchmentId = candidate;
            return globalCatchmentId != 0UL;
        }

        private static bool FiniteBounds(Bounds bounds) =>
            Finite(bounds.center) && Finite(bounds.size) &&
            bounds.size.x > 0f && bounds.size.y > 0f && bounds.size.z > 0f;

        internal bool PublishCapacityStorage(
            VolumetricPceCapacityStorageDescriptor descriptor, out string error)
        {
            error = string.Empty;
            if (descriptor == null || !descriptor.Validate(out error)) return false;
            if (string.IsNullOrWhiteSpace(descriptor.SourcePartitionId))
            {
                error = "PCE capacity publication requires a stable source-partition identity.";
                return false;
            }
            if (_capacityStorageByPartition.TryGetValue(descriptor.SourcePartitionId, out VolumetricPceCapacityStorageDescriptor previous) &&
                descriptor.GeometryRevision < previous.GeometryRevision)
            {
                error = "PCE capacity publication is older than the retained geometry revision.";
                return false;
            }
            _capacityStorageByPartition[descriptor.SourcePartitionId] = descriptor;
            CapacityStoragePublished?.Invoke(descriptor);
            return true;
        }

        internal bool PublishAppliedDomainCapacityStorage(
            VolumetricWaterDomain domain,
            CodyCatchmentDescriptor catchment,
            long geometryRevision,
            out string error)
        {
            error = string.Empty;
            if (domain == null || catchment == null)
            {
                error = "PCE capacity publication requires an applied domain and CODY catchment.";
                return false;
            }
            VolumetricWaterSettings settings = domain.Settings;
            int cellCount = settings.ResolutionX * settings.ResolutionY * settings.ResolutionZ;
            float[] cellCapacity = domain.CaptureCutCellCapacitySync();
            Vector2[] storageCurves = domain.CaptureHydraulicStorageSparseSync();
            int[] storageKnotCounts = domain.CaptureHydraulicStorageSparseCountsSync();
            float[] apertureU = domain.CaptureHydraulicApertureUSync();
            float[] apertureW = domain.CaptureHydraulicApertureWSync();
            BuildAppliedCellMembership(
                cellCapacity, storageCurves, storageKnotCounts, apertureU, apertureW,
                settings.ResolutionX, settings.ResolutionY, settings.ResolutionZ,
                catchment.CatchmentId, domain.WorldOrigin, settings.CellSize,
                out ulong[] cellCatchmentIds, out int[] cellComponentIds);
            CodyDrainageExit[] drainageExits = catchment.DrainageExits ?? Array.Empty<CodyDrainageExit>();
            var exits = new VolumetricPceStorageExit[drainageExits.Length];
            for (int i = 0; i < drainageExits.Length; i++)
            {
                exits[i] = new VolumetricPceStorageExit
                {
                    DestinationRegionX = drainageExits[i].DestinationRegionX,
                    DestinationRegionZ = drainageExits[i].DestinationRegionZ,
                    SaddleHeight = drainageExits[i].SpillHeight,
                    PathLength = drainageExits[i].PathLength,
                    MinimumApertureEquivalent = drainageExits[i].MinimumApertureEquivalent
                };
            }
            var descriptor = new VolumetricPceCapacityStorageDescriptor
            {
                CatchmentId = catchment.CatchmentId,
                SourcePartitionId = "active-window:" + catchment.CatchmentId + ":" +
                    Mathf.RoundToInt(domain.WorldOrigin.x / settings.CellSize) + ":" +
                    Mathf.RoundToInt(domain.WorldOrigin.z / settings.CellSize),
                CompleteSourceDomain = false,
                GeometryRevision = geometryRevision,
                DependencyRevisionHash = catchment.DependencyRevisionHash,
                WorldBounds = new Bounds(domain.WorldOrigin + new Vector3(
                    settings.ResolutionX * settings.CellSize,
                    settings.ResolutionY * settings.CellSize,
                    settings.ResolutionZ * settings.CellSize) * 0.5f,
                    new Vector3(settings.ResolutionX, settings.ResolutionY, settings.ResolutionZ) * settings.CellSize),
                GridWorldOrigin = domain.WorldOrigin,
                CellSize = settings.CellSize,
                ResolutionX = settings.ResolutionX,
                ResolutionY = settings.ResolutionY,
                ResolutionZ = settings.ResolutionZ,
                CellCapacity = cellCapacity,
                CellStorageCurves = storageCurves,
                CellStorageKnotCounts = storageKnotCounts,
                CellCatchmentIds = cellCatchmentIds,
                CellComponentIds = cellComponentIds,
                Exits = exits
            };
            if (!PublishCapacityStorage(descriptor, out error)) return false;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore PCE published geometry-only storage: catchment=" + catchment.CatchmentId +
                ", geometryRevision=" + geometryRevision + ", dependency=" + catchment.DependencyRevisionHash +
                ", cells=" + descriptor.CellCapacity.Length + ", sparseKnots=" + descriptor.CellStorageCurves.Length + ".");
            return true;
        }

        private static void BuildAppliedCellMembership(
            float[] capacity, Vector2[] storageCurves, int[] storageKnotCounts,
            float[] apertureU, float[] apertureW,
            int nx, int ny, int nz, ulong catchmentId, Vector3 origin, float cellSize,
            out ulong[] cellCatchmentIds, out int[] cellComponentIds)
        {
            int count = nx * ny * nz;
            cellCatchmentIds = new ulong[count];
            cellComponentIds = new int[count];
            int[] components = cellComponentIds;
            for (int i = 0; i < count; i++) components[i] = -1;
            if (capacity.Length != count || apertureU.Length != (nx + 1) * ny * nz ||
                apertureW.Length != nx * ny * (nz + 1)) return;
            var queue = new Queue<int>();
            for (int seed = 0; seed < count; seed++)
            {
                if (capacity[seed] <= 1e-6f || components[seed] >= 0) continue;
                int component = seed;
                components[seed] = component;
                queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = current % nx;
                    int y = (current / nx) % ny;
                    int z = current / (nx * ny);
                    TryVisit(x - 1, y, z, apertureU[(x) + (nx + 1) * (y + ny * z)]);
                    TryVisit(x + 1, y, z, apertureU[(x + 1) + (nx + 1) * (y + ny * z)]);
                    TryVisit(x, y - 1, z, HasVerticalConnection(current - nx, current, storageCurves, storageKnotCounts));
                    TryVisit(x, y + 1, z, HasVerticalConnection(current, current + nx, storageCurves, storageKnotCounts));
                    TryVisit(x, y, z - 1, apertureW[x + nx * (y + ny * z)]);
                    TryVisit(x, y, z + 1, apertureW[x + nx * (y + ny * (z + 1))]);

                    void TryVisit(int nx2, int ny2, int nz2, float face)
                    {
                        if (face <= 0f || nx2 < 0 || nx2 >= nx || ny2 < 0 || ny2 >= ny || nz2 < 0 || nz2 >= nz) return;
                        int next = nx2 + nx * (ny2 + ny * nz2);
                        if (capacity[next] <= 1e-6f || components[next] >= 0) return;
                        components[next] = component;
                        queue.Enqueue(next);
                    }
                }
            }
            int centerX = Mathf.Clamp(Mathf.FloorToInt((origin.x + nx * cellSize * 0.5f - origin.x) / cellSize), 0, nx - 1);
            int centerY = Mathf.Clamp(Mathf.FloorToInt((origin.y + ny * cellSize * 0.5f - origin.y) / cellSize), 0, ny - 1);
            int centerZ = Mathf.Clamp(Mathf.FloorToInt((origin.z + nz * cellSize * 0.5f - origin.z) / cellSize), 0, nz - 1);
            int center = centerX + nx * (centerY + ny * centerZ);
            int ownedComponent = center >= 0 && center < count ? components[center] : -1;
            if (ownedComponent < 0) return;
            for (int i = 0; i < count; i++)
                if (components[i] == ownedComponent) cellCatchmentIds[i] = catchmentId;
        }

        private static float HasVerticalConnection(int lower, int upper, Vector2[] curves, int[] counts)
        {
            if (curves == null || counts == null || lower < 0 || upper < 0 ||
                lower >= counts.Length || upper >= counts.Length) return 0f;
            int stride = VolumetricCutCellProjection.AdaptiveHydraulicCurveKnotCount;
            int lowerCount = Mathf.Clamp(counts[lower], 2, stride);
            int upperCount = Mathf.Clamp(counts[upper], 2, stride);
            float lowerTop = EvaluateStorageCurve(curves, lower * stride, lowerCount, 0.99f);
            float lowerNearTop = EvaluateStorageCurve(curves, lower * stride, lowerCount, 0.90f);
            float upperNearBottom = EvaluateStorageCurve(curves, upper * stride, upperCount, 0.10f);
            float upperBottom = EvaluateStorageCurve(curves, upper * stride, upperCount, 0.01f);
            return lowerTop - lowerNearTop > 1e-4f && upperNearBottom - upperBottom > 1e-4f ? 1f : 0f;
        }

        private static float EvaluateStorageCurve(Vector2[] curves, int offset, int count, float height)
        {
            if (height <= curves[offset].x) return curves[offset].y;
            for (int i = 1; i < count; i++)
            {
                Vector2 b = curves[offset + i];
                Vector2 a = curves[offset + i - 1];
                if (height <= b.x)
                {
                    float span = b.x - a.x;
                    return span <= 1e-7f ? b.y : Mathf.Lerp(a.y, b.y, (height - a.x) / span);
                }
            }
            return curves[offset + count - 1].y;
        }

        private void Awake()
        {
            _geometry = new ProbeColonyGeometryBridge(_world, new Vector3Int(32, 32, 32));
            _queue = new ProbeColonyChangeQueue(_world, new Vector3Int(32, 32, 32));
            _events = new ProbeColonyEventBridge(_queue);
            InitializeCody();
            Instance = this;
            Attach(PhysicalWaterValheimWorldGeometryAdapter.Instance);
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE runtime created. ChunkSize=(32,32,32), event-driven adapter bridge active.");
        }

        private void Update()
        {
            PhysicalWaterValheimWorldGeometryAdapter current = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (current != _adapter)
            {
                if (_adapter != null) _adapter.PceGeometryChanged -= OnGeometryChanged;
                _adapter = current;
                if (_adapter != null) _adapter.PceGeometryChanged += OnGeometryChanged;
            }
            if (_queue.PendingCount > 0) _queue.Drain();
            if (_codyRebuilds != null) _codyRebuilds.PublishReady();
            DispatchPendingCodyCoverage();
            TryApplyConfiguredSourceMarker();
            TryAttachSourceCatchmentToPublishedDomain();
            TryBootstrapCompleteBaseWorldDomain();
        }

        private void TryApplyConfiguredSourceMarker()
        {
            if (_configuredSourceMarkerApplied || _codyL2 == null ||
                PhysicalWaterPlugin.Settings == null) return;
            string configured = PhysicalWaterPlugin.Settings.InitialWorldPceSourceCatchmentId.Value;
            if (string.IsNullOrWhiteSpace(configured))
            {
                _configuredSourceMarkerApplied = true;
                return;
            }
            if (!TryParseCatchmentId(configured, out ulong catchmentId))
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore ignored invalid InitialWorldPce.SourceCatchmentId; bootstrap remains fail-closed.");
                _configuredSourceMarkerApplied = true;
                return;
            }
            if (!_codyL2.TryPublishInitialWaterSourceSeed(catchmentId, out string error))
            {
                if (!_configuredSourceMarkerReported)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore explicit CODY source marker was not applied: " + error + ".");
                    _configuredSourceMarkerReported = true;
                }
                return;
            }
            _configuredSourceMarkerApplied = true;
            _codyPersistenceWritable = true;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore applied explicit CODY initial-water source marker: catchment=" + catchmentId + ".");
        }

        private void RearmConfiguredSourceMarkerIfAffected(
            IReadOnlyList<ulong> affectedCatchmentIds)
        {
            if (affectedCatchmentIds == null || PhysicalWaterPlugin.Settings == null) return;
            if (!TryParseCatchmentId(
                    PhysicalWaterPlugin.Settings.InitialWorldPceSourceCatchmentId.Value,
                    out ulong configuredId)) return;
            for (int i = 0; i < affectedCatchmentIds.Count; i++)
            {
                if (affectedCatchmentIds[i] != configuredId) continue;
                // The invalidated descriptor cannot carry a valid marker.
                // Re-arm only the explicit configured identity; the next
                // Update may apply it after CODY publishes a valid rebuild.
                _configuredSourceMarkerApplied = false;
                _configuredSourceMarkerReported = false;
                return;
            }
        }

        internal bool TrySelectInitialWorldSourceCatchment(ulong catchmentId, out string error)
        {
            error = string.Empty;
            if (catchmentId == 0UL || _codyL2 == null)
            {
                error = "CODY initial-water source selection is not ready.";
                return false;
            }
            if (!_codyL2.TryPublishInitialWaterSourceSeed(catchmentId, out error)) return false;
            _configuredSourceMarkerApplied = true;
            _codyPersistenceWritable = true;
            TryAttachSourceCatchmentToPublishedDomain();
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore selected explicit CODY initial-water source catchment: catchment=" + catchmentId + ".");
            return true;
        }

        internal string DescribeCodyCatchments()
        {
            if (_codyL2 == null) return "CODY L2 is not initialized.";
            CodyCatchmentDescriptor[] descriptors = _codyL2.CapturePersistentDescriptors();
            if (descriptors == null || descriptors.Length == 0) return "CODY L2 has no valid persisted catchments.";
            var builder = new System.Text.StringBuilder("CODY catchments: ");
            for (int i = 0; i < descriptors.Length; i++)
            {
                if (i > 0) builder.Append("; ");
                CodyCatchmentDescriptor descriptor = descriptors[i];
                builder.Append(descriptor.CatchmentId)
                    .Append(" region=(").Append(descriptor.RegionX).Append(',').Append(descriptor.RegionZ)
                    .Append(") sourceSeed=").Append(descriptor.InitialWaterSourceSeed);
            }
            return builder.ToString();
        }

        internal static bool TryParseCatchmentId(string text, out ulong value)
        {
            value = 0UL;
            string normalized = text.Trim();
            if (!normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                ulong.TryParse(normalized, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out value) && value != 0UL)
                return true;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(2);
            return ulong.TryParse(normalized,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out value) && value != 0UL;
        }

        private void TryAttachSourceCatchmentToPublishedDomain()
        {
            LiquidCoreInitialWorldWaterDomain domain = _completeInitialWorldDomain;
            if (domain == null || domain.SourceCatchmentId != 0UL ||
                (domain.SourceCatchmentIds != null && domain.SourceCatchmentIds.Length > 0)) return;
            if (TryResolvePersistedSourceRelation(
                    _baseWorldBootstrapWorldKey, domain.GeometryRevision, domain.DependencyRevisionHash,
                    domain.Partitions, out ulong[] persistedIds, out _))
            {
                domain.SourceCatchmentIds = persistedIds;
                domain.SourceCatchmentId = persistedIds.Length == 1 ? persistedIds[0] : 0UL;
                CompleteInitialWorldDomainPublished?.Invoke(domain.Clone());
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore replayed the persisted CODY initial-water source relation: catchments=" +
                    string.Join(",", Array.ConvertAll(persistedIds, id => id.ToString())) + ".");
                return;
            }
            if (!TryGetCodyInitialWaterSourceSeed(
                    out CodyCatchmentDescriptor sourceSeed, out string sourceError)) return;
            if (!TryResolveClosedPceCatchment(
                    sourceSeed, domain.Partitions, out ulong globalSourceCatchmentId,
                    out sourceError)) return;
            domain.SourceCatchmentId = globalSourceCatchmentId;
            domain.SourceCatchmentIds = new[] { globalSourceCatchmentId };
            CompleteInitialWorldDomainPublished?.Invoke(domain.Clone());
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore attached the explicit CODY initial-water source catchment to the published PCE domain: catchment=" +
                globalSourceCatchmentId + ".");
        }

        private void TryBootstrapCompleteBaseWorldDomain()
        {
            if (PhysicalWaterPlugin.Settings == null)
            {
                ReportBaseWorldBootstrapGate("settings-unavailable");
                return;
            }
            if (!PhysicalWaterPlugin.Settings.StageE1Enabled.Value)
            {
                ReportBaseWorldBootstrapGate("stage-e1-disabled");
                return;
            }
            if (WorldGenerator.instance == null)
            {
                ReportBaseWorldBootstrapGate("world-generator-unavailable");
                return;
            }
            World world = ZNet.GetWorldIfIsHost();
            if (world == null)
            {
                ReportBaseWorldBootstrapGate("host-world-unavailable");
                return;
            }
            if (world.m_uid == 0L)
            {
                ReportBaseWorldBootstrapGate("host-world-uid-unavailable");
                return;
            }
            ReportBaseWorldBootstrapGate("ready:" + world.m_uid.ToString("X16"));
            string worldKey = world.m_uid.ToString("X16");
            if (_baseWorldPceBootstrapJob != null)
            {
                if (!string.Equals(_baseWorldPceBootstrapJob.WorldKey, worldKey, StringComparison.Ordinal))
                {
                    _baseWorldPceBootstrapJob = null;
                    _baseWorldBootstrapDomainFingerprint = null;
                    _baseWorldBootstrapAttemptKey = null;
                }
                else
                {
                    AdvanceBaseWorldPceBootstrapJob();
                    return;
                }
            }
            float verticalMin = PhysicalWaterPlugin.Settings.InitialWorldPceVerticalMin.Value;
            float verticalMax = PhysicalWaterPlugin.Settings.InitialWorldPceVerticalMax.Value;
            float partitionSize = PhysicalWaterPlugin.Settings.InitialWorldPcePartitionSize.Value;
            float cellSize = PhysicalWaterPlugin.Settings.InitialWorldPceCellSize.Value;
            if (!Finite(verticalMin) || !Finite(verticalMax) || verticalMax <= verticalMin ||
                !Finite(partitionSize) || partitionSize <= 0f || !Finite(cellSize) || cellSize <= 0f)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore complete base-world PCE bootstrap rejected invalid explicit domain settings.");
                _baseWorldBootstrapWorldKey = worldKey;
                return;
            }
            long geometryRevision = Math.Max(0, world.m_worldGenVersion);
            string dependencyRevision = BuildBaseWorldDependencyRevision(
                world, verticalMin, verticalMax, partitionSize, cellSize);
            if (!TryEnumerateBaseWorldPcePartitions(
                    verticalMin, verticalMax, new Vector2(partitionSize, partitionSize),
                    out Bounds sourceBounds, out Bounds[] partitionBounds, out string error))
            {
                if (!_baseWorldBootstrapFailureReported)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + error + ".");
                    _baseWorldBootstrapFailureReported = true;
                }
                return;
            }
            string domainFingerprint = BuildBaseWorldDomainFingerprint(
                sourceBounds, partitionSize, cellSize);
            if (string.Equals(_baseWorldBootstrapWorldKey, worldKey, StringComparison.Ordinal) &&
                string.Equals(_baseWorldBootstrapDomainFingerprint, domainFingerprint, StringComparison.Ordinal))
                return;
            string attemptKey = worldKey + ":" + domainFingerprint + ":" +
                geometryRevision + ":" +
                verticalMin.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                verticalMax.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                partitionSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                cellSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (string.Equals(_baseWorldBootstrapAttemptKey, attemptKey, StringComparison.Ordinal)) return;
            _baseWorldBootstrapAttemptKey = attemptKey;
            _baseWorldBootstrapFailureReported = false;
            _baseWorldPceBootstrapJob = new BaseWorldPceBootstrapJob
            {
                WorldKey = worldKey,
                DomainId = "valheim-world-ocean-" + worldKey,
                SourceCatchmentId = 0UL,
                SourceBounds = sourceBounds,
                PartitionBounds = partitionBounds,
                PartitionSize = new Vector2(partitionSize, partitionSize),
                CellSize = cellSize,
                GeometryRevision = geometryRevision,
                DependencyRevisionHash = dependencyRevision,
                EstimatedCompactGeometryBytes = EstimateBaseWorldCompactGeometryBytes(
                    sourceBounds, partitionBounds.Length, cellSize)
            };
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore complete base-world PCE bootstrap started: bounds=" +
                sourceBounds.min + ".." + sourceBounds.max + ", partitions=" + partitionBounds.Length +
                ", estimated compact geometry=" +
                _baseWorldPceBootstrapJob.EstimatedCompactGeometryBytes + " bytes.");
            AdvanceBaseWorldPceBootstrapJob();
        }

        private void ReportBaseWorldBootstrapGate(string state)
        {
            if (string.Equals(_baseWorldBootstrapGateState, state, StringComparison.Ordinal)) return;
            _baseWorldBootstrapGateState = state;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore complete base-world PCE bootstrap gate: state=" + state + ".");
        }

        private bool TryCloseBaseWorldPcePartitionsStreaming(
            BaseWorldPceBootstrapJob job,
            List<VolumetricPceCapacityStorageDescriptor> closed,
            out string error)
        {
            error = string.Empty;
            if (job == null || closed == null)
            {
                error = "PCE streaming closure received no bootstrap job or sink.";
                return false;
            }
            return VolumetricPceGlobalConnectivityClosure.TryCloseStreaming(
                job.SourceBounds, job.PartitionSize, job.GeometryRevision,
                job.DependencyRevisionHash, job.PartitionBounds.Length,
                (int index, out VolumetricPceCapacityStorageDescriptor descriptor,
                    out string loadError) => TryBuildBaseTerrainPcePartition(
                        job.PartitionBounds[index], job.PartitionSize, job.CellSize,
                        job.GeometryRevision, job.DependencyRevisionHash,
                        out descriptor, out loadError),
                (int index, VolumetricPceCapacityStorageDescriptor descriptor,
                    out string sinkError) =>
                {
                    sinkError = string.Empty;
                    if (descriptor == null)
                    {
                        sinkError = "PCE streaming closure produced a null partition.";
                        return false;
                    }
                    closed.Add(descriptor);
                    return true;
                }, out error);
        }

        private static long EstimateBaseWorldCompactGeometryBytes(
            Bounds sourceBounds, int partitionCount, float cellSize)
        {
            if (partitionCount <= 0 || !Finite(cellSize) || cellSize <= 0f) return 0L;
            long horizontalCells = (long)Math.Ceiling(sourceBounds.size.x / cellSize) *
                                   (long)Math.Ceiling(sourceBounds.size.z / cellSize);
            long verticalCells = (long)Math.Ceiling(sourceBounds.size.y / cellSize);
            long cells = horizontalCells * verticalCells;
            // Compact base terrain retains capacity, local component IDs,
            // final catchment IDs, and one terrain height per X/Z column.
            long membershipBytes = cells * (sizeof(float) + sizeof(int) + sizeof(ulong));
            long heightBytes = horizontalCells * sizeof(float);
            if (cells < 0L || membershipBytes < 0L || heightBytes < 0L ||
                membershipBytes > long.MaxValue - heightBytes) return long.MaxValue;
            return membershipBytes + heightBytes;
        }

        private void AdvanceBaseWorldPceBootstrapJob()
        {
            BaseWorldPceBootstrapJob job = _baseWorldPceBootstrapJob;
            if (job == null) return;
            if (job.NextPartition < job.PartitionBounds.Length)
            {
                if (!TryBuildBaseTerrainPcePartition(
                        job.PartitionBounds[job.NextPartition], job.PartitionSize,
                        job.CellSize, job.GeometryRevision, job.DependencyRevisionHash,
                        out VolumetricPceCapacityStorageDescriptor descriptor, out string error))
                {
                    _baseWorldPceBootstrapJob = null;
                    if (!_baseWorldBootstrapFailureReported)
                    {
                        PhysicalWaterPlugin.Log.LogWarning(
                            "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + error + ".");
                        _baseWorldBootstrapFailureReported = true;
                    }
                    return;
                }
                job.ProvisionalPartitions.Add(descriptor);
                job.NextPartition++;
                if (job.NextPartition == 1 ||
                    job.NextPartition == job.PartitionBounds.Length ||
                    (job.NextPartition % 16) == 0)
                {
                    PhysicalWaterPlugin.Log.LogInfo(
                        "LiquidCore complete base-world PCE partition sampled: " +
                        job.NextPartition + "/" + job.PartitionBounds.Length +
                        ", id=" + descriptor.SourcePartitionId +
                        ", cells=" + descriptor.CellCapacity.Length + ".");
                }
                return;
            }
            VolumetricPceCapacityStorageDescriptor[] closed;
            string closureError = string.Empty;
            string sourceError = string.Empty;
            string assemblyError = string.Empty;
            CodyCatchmentDescriptor sourceSeed = null;
            ulong globalSourceCatchmentId = 0UL;
            LiquidCoreInitialWorldWaterDomain domain = null;
            if (_baseWorldPceClosureTask == null)
            {
                BaseWorldPceBootstrapJob closureJob = job;
                _baseWorldPceClosureTask = Task.Run(() =>
                {
                    long managedBefore = GC.GetTotalMemory(false);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    bool resultValid = VolumetricPceGlobalConnectivityClosure.TryCloseOwned(
                        closureJob.SourceBounds, closureJob.PartitionSize,
                        closureJob.GeometryRevision, closureJob.DependencyRevisionHash,
                        closureJob.ProvisionalPartitions, out VolumetricPceCapacityStorageDescriptor[] resultClosed,
                        out string resultError);
                    watch.Stop();
                    return new BaseWorldPceClosureResult
                    {
                        Job = closureJob,
                        Valid = resultValid,
                        Closed = resultClosed,
                        Error = resultError,
                        ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
                        ManagedBefore = managedBefore,
                        ManagedAfter = GC.GetTotalMemory(false)
                    };
                });
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore complete base-world PCE closure scheduled off-thread: partitions=" +
                    job.PartitionBounds.Length + ".");
                return;
            }
            if (!_baseWorldPceClosureTask.IsCompleted) return;
            BaseWorldPceClosureResult closureResult;
            try
            {
                closureResult = _baseWorldPceClosureTask.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _baseWorldPceClosureTask = null;
                _baseWorldPceBootstrapJob = null;
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore complete base-world PCE closure failed closed off-thread: " + ex.Message + ".");
                return;
            }
            _baseWorldPceClosureTask = null;
            if (!ReferenceEquals(closureResult.Job, job)) return;
            closed = closureResult.Closed ?? Array.Empty<VolumetricPceCapacityStorageDescriptor>();
            closureError = closureResult.Error ?? string.Empty;
            bool valid = closureResult.Valid;
            string failure = valid ? string.Empty : closureError;
            if (valid)
            {
                valid = VolumetricPceCompletePartitionAssembler.TryAssembleOwned(
                    job.DomainId, job.SourceBounds, job.PartitionSize,
                    job.GeometryRevision, job.DependencyRevisionHash, closed,
                    out domain, out assemblyError);
            }
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore complete base-world PCE closure telemetry: partitions=" +
                job.PartitionBounds.Length + ", valid=" + valid + ", elapsedMs=" +
                closureResult.ElapsedMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                ", managedBefore=" + closureResult.ManagedBefore + ", managedAfter=" + closureResult.ManagedAfter +
                ", managedDelta=" + (closureResult.ManagedAfter - closureResult.ManagedBefore) + ".");
            if (!valid)
            {
                _baseWorldPceBootstrapJob = null;
                if (string.IsNullOrEmpty(failure)) failure = sourceError;
                if (string.IsNullOrEmpty(failure)) failure = assemblyError;
                if (!_baseWorldBootstrapFailureReported)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + failure + ".");
                    _baseWorldBootstrapFailureReported = true;
                }
                return;
            }
            // Geometry publication is independent of LiquidCore's one-time
            // source selection. If an explicit CODY source marker already
            // exists, attach its globally closed component now; otherwise the
            // published domain remains source-unselected and the attachment is
            // retried when the marker arrives.
            if (TryResolvePersistedSourceRelation(
                    job.WorldKey, job.GeometryRevision, job.DependencyRevisionHash,
                    closed, out ulong[] persistedSourceIds, out string persistedSourceError))
            {
                domain.SourceCatchmentIds = persistedSourceIds;
                domain.SourceCatchmentId = persistedSourceIds.Length == 1 ? persistedSourceIds[0] : 0UL;
                sourceError = string.Empty;
            }
            else if (TryGetCodyInitialWaterSourceSeed(out sourceSeed, out sourceError) &&
                     TryResolveClosedPceCatchment(sourceSeed, closed,
                         out globalSourceCatchmentId, out sourceError))
            {
                domain.SourceCatchmentId = globalSourceCatchmentId;
                domain.SourceCatchmentIds = new[] { globalSourceCatchmentId };
                PersistResolvedSourceRelation(job, domain.SourceCatchmentIds);
            }
            else if (TryResolveClosedPceInitialSourceSet(
                         job.SourceBounds, closed, out ulong[] resolvedSourceIds,
                         out string resolvedSourceError))
            {
                // CODY records the post-closure world-exterior relation; it
                // does not own water. SeaLevel is consumed only here as the
                // explicit one-time initial reference head.
                domain.SourceCatchmentIds = resolvedSourceIds;
                domain.SourceCatchmentId = resolvedSourceIds.Length == 1 ? resolvedSourceIds[0] : 0UL;
                PersistResolvedSourceRelation(job, resolvedSourceIds);
                sourceError = string.Empty;
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore CODY recorded the post-closure initial-world source relation: catchments=" +
                    string.Join(",", Array.ConvertAll(resolvedSourceIds, id => id.ToString())) + ".");
            }
            else
            {
                domain.SourceCatchmentId = 0UL;
                domain.SourceCatchmentIds = Array.Empty<ulong>();
                if (string.IsNullOrEmpty(sourceError)) sourceError = persistedSourceError;
                if (string.IsNullOrEmpty(sourceError)) sourceError = resolvedSourceError;
                LogUnselectedClosedPceCatchments(closed, sourceError);
            }
            if (!CanPublishCompleteBaseWorldPceDomain(domain, closed, out string preflightError))
            {
                _baseWorldPceBootstrapJob = null;
                if (!_baseWorldBootstrapFailureReported)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + preflightError + ".");
                    _baseWorldBootstrapFailureReported = true;
                }
                return;
            }
            for (int i = 0; i < closed.Length; i++)
            {
                if (!PublishCapacityStorage(closed[i], out string publishError))
                {
                    _baseWorldPceBootstrapJob = null;
                    if (!_baseWorldBootstrapFailureReported)
                    {
                        PhysicalWaterPlugin.Log.LogWarning(
                            "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + publishError + ".");
                        _baseWorldBootstrapFailureReported = true;
                    }
                    return;
                }
            }
            if (!PublishCompleteInitialWorldDomainOwned(domain, out string domainError))
            {
                _baseWorldPceBootstrapJob = null;
                if (!_baseWorldBootstrapFailureReported)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore complete base-world PCE bootstrap deferred/fail-closed: " + domainError + ".");
                    _baseWorldBootstrapFailureReported = true;
                }
                return;
            }
            _baseWorldPceBootstrapJob = null;
            _baseWorldBootstrapWorldKey = job.WorldKey;
            _baseWorldBootstrapDomainFingerprint = BuildBaseWorldDomainFingerprint(
                job.SourceBounds, job.PartitionSize.x, job.CellSize);
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore complete base-world PCE domain published: world=" + job.WorldKey +
                ", partitions=" + closed.Length + ", geometryRevision=" + job.GeometryRevision + ".");
        }

        private static void LogUnselectedClosedPceCatchments(
            IReadOnlyList<VolumetricPceCapacityStorageDescriptor> closed,
            string sourceError)
        {
            var ids = new SortedSet<ulong>();
            if (closed != null)
            {
                for (int i = 0; i < closed.Count; i++)
                {
                    ulong[] membership = closed[i]?.CellCatchmentIds;
                    if (membership == null) continue;
                    for (int j = 0; j < membership.Length; j++)
                        if (membership[j] != 0UL) ids.Add(membership[j]);
                }
            }
            string[] values = new string[Math.Min(ids.Count, 16)];
            int index = 0;
            foreach (ulong id in ids)
            {
                if (index >= values.Length) break;
                values[index++] = id.ToString("X16");
            }
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore complete base-world PCE source remains unselected: " +
                "explicit CODY source marker is required; closedCatchmentCount=" + ids.Count +
                ", closedCatchments=" + string.Join(",", values) +
                ", reason=" + (string.IsNullOrEmpty(sourceError) ? "no validated CODY source seed" : sourceError) + ".");
        }

        private static string BuildBaseWorldDependencyRevision(
            World world, float verticalMin, float verticalMax,
            float partitionSize, float cellSize)
        {
            return "valheim-worldgen:" + world.m_seed + ":" + world.m_worldGenVersion +
                ":vertical=" + verticalMin.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ":" + verticalMax.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ":partition=" + partitionSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                ":cell=" + cellSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string BuildBaseWorldDomainFingerprint(
            Bounds sourceBounds, float partitionSize, float cellSize)
        {
            System.Globalization.CultureInfo culture =
                System.Globalization.CultureInfo.InvariantCulture;
            return sourceBounds.min.x.ToString("R", culture) + ":" +
                sourceBounds.min.y.ToString("R", culture) + ":" +
                sourceBounds.min.z.ToString("R", culture) + ":" +
                sourceBounds.max.x.ToString("R", culture) + ":" +
                sourceBounds.max.y.ToString("R", culture) + ":" +
                sourceBounds.max.z.ToString("R", culture) + ":partition=" +
                partitionSize.ToString("R", culture) + ":cell=" +
                cellSize.ToString("R", culture);
        }

        internal void Attach(PhysicalWaterValheimWorldGeometryAdapter adapter)
        {
            if (adapter == null || adapter == _adapter) return;
            if (_adapter != null) _adapter.PceGeometryChanged -= OnGeometryChanged;
            _adapter = adapter;
            _adapter.PceGeometryChanged += OnGeometryChanged;
        }

        private void OnGeometryChanged(ValheimPceGeometryChange change)
        {
            SourceRuntimeRecord runtimeRecord = ResolveSourceRuntimeRecord(change);
            ApplyCodyDependencyChange(change);
            VolumetricWorldGeometryVoxelRegion region = new VolumetricWorldGeometryVoxelRegion { Min = change.DirtyMin, Max = change.DirtyMax, Valid = true };
            VolumetricWorldGeometryCategory pceCategory = ToPceCategory(change.Category);
            bool accepted = _causalSignals.Publish(new ProbeColonyCausalGeometrySignal
            {
                SourceId = change.SourceId,
                AssetClassId = runtimeRecord.AssetClassId,
                ChangeKind = change.ChangeKind == ValheimWorldGeometryChangeKind.Added
                    ? VolumetricWorldGeometryChangeKind.Added
                    : change.ChangeKind == ValheimWorldGeometryChangeKind.Removed
                        ? VolumetricWorldGeometryChangeKind.Removed
                        : VolumetricWorldGeometryChangeKind.MovedOrChanged,
                Category = pceCategory,
                SourceRevision = change.Revision,
                SourceRuntimeHandle = runtimeRecord.PceOccupancyHandle,
                DependencyHandle = runtimeRecord.LcPreparedGeometryHandle,
                PreparedGeometry = runtimeRecord.PreparedGeometry,
                DatabaseHit = runtimeRecord.DatabaseHit,
                Generation = change.Generation,
                Root = change.Root,
                RootInstanceId = change.RootInstanceId,
                OldWorldBounds = change.OldWorldBounds,
                NewWorldBounds = change.NewWorldBounds,
                HasOldWorldBounds = change.HasOldWorldBounds,
                HasNewWorldBounds = change.HasNewWorldBounds,
                EventTimestamp = change.EventTimestamp,
                ReadyTimestamp = change.ReadyTimestamp
            });
            if (!accepted)
            {
                if (PhysicalWaterPlugin.Settings != null && PhysicalWaterPlugin.Settings.Diagnostics.Value)
                    PhysicalWaterPlugin.Log.LogInfo(
                        "LiquidCore PCE instance-cache reuse: source=" + change.SourceId +
                        ", revision=" + change.Revision +
                        "; no probe update or LC geometry signal emitted.");
                return;
            }
            _publishedEvents++;
            if (runtimeRecord.DatabaseHit) _directQueueBypasses++;
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE geometry event #" + _publishedEvents + ": source=" + change.SourceId + ", asset=" + runtimeRecord.AssetClassId + ", database=" + (runtimeRecord.DatabaseHit ? "hit" : "miss") + ", directQueueBypass=" + runtimeRecord.DatabaseHit + ", directQueueBypasses=" + _directQueueBypasses + ", category=" + change.Category + ", change=" + change.ChangeKind + ", revision=" + change.Revision + ", kind=" + change.Kind + ", root=" + change.RootType + ", region=" + change.DirtyMin + ".." + change.DirtyMax + ".");
            if (change.ChangeKind == ValheimWorldGeometryChangeKind.Removed)
            {
                // Known callbacks already updated the shared runtime record and
                // published the direct LC delta above. Do not mirror that same
                // event into the approximate discovery queue.
                if (!runtimeRecord.DatabaseHit)
                    _events.PublishRemoved(change.SourceId, pceCategory, region, change.Revision);
                _activeSources.Remove(change.SourceId);
                _geometry.RemoveSource(change.SourceId);
                return;
            }

            _activeSources[change.SourceId] = change;
            if (!runtimeRecord.DatabaseHit)
            {
                if (change.ChangeKind == ValheimWorldGeometryChangeKind.Added)
                    _events.PublishAdded(change.SourceId, pceCategory, region, change.Revision);
                else
                    _events.PublishChanged(change.SourceId, pceCategory, region, change.Revision);
            }
            // The retained source/runtime record is PCE's authoritative dormant
            // state and already carries the exact LC descriptor. Do not eagerly
            // materialize a duplicate probe-cell field: no solver consumer reads
            // it, and Physics.ClosestPoint across every source made the causal
            // gate synchronous with unrelated dormant probes.
        }

        internal bool TryGetActiveSource(string sourceId, out ValheimPceGeometryChange change) => _activeSources.TryGetValue(sourceId, out change);

        /// <summary>
        /// Bounded PCE provenance lookup used only after the exact cut-cell
        /// topology finds a possible sub-grid obstruction. Bounds are a
        /// conservative fail-closed classifier: any real object source wins
        /// over overlapping Heightmap ground; unknown geometry is never treated
        /// as a natural lip.
        /// </summary>
        internal LiquidCoreSubGridObstacleClass ClassifySubGridObstacle(Bounds worldCell)
        {
            bool naturalGround = false;
            foreach (ValheimPceGeometryChange source in _activeSources.Values)
            {
                if (!source.CanFeedSdf || !source.HasNewWorldBounds ||
                    !source.NewWorldBounds.Intersects(worldCell)) continue;
                switch (source.Category)
                {
                    case ValheimWorldGeometryCategory.Terrain:
                        naturalGround = true;
                        break;
                    case ValheimWorldGeometryCategory.SolidBarrier:
                    case ValheimWorldGeometryCategory.ThinBlockingBarrier:
                    case ValheimWorldGeometryCategory.DynamicSolid:
                    case ValheimWorldGeometryCategory.Unsupported:
                        return LiquidCoreSubGridObstacleClass.RealBarrier;
                }
            }
            return naturalGround
                ? LiquidCoreSubGridObstacleClass.NaturalGround
                : LiquidCoreSubGridObstacleClass.Unknown;
        }

        internal int WarmCodyCatchments(Bounds worldBounds)
        {
            if (_codyL1 == null || _codyL2 == null) return 0;
            _codyWarmBounds = worldBounds;
            _hasCodyWarmBounds = true;
            _codyL1.Clear();
            CodyCatchmentDescriptor[] descriptors = _codyL2.CapturePersistentDescriptors();
            int warmed = 0;
            for (int i = 0; i < descriptors.Length; i++)
            {
                if (!descriptors[i].DependencyBounds.Intersects(worldBounds)) continue;
                _codyL1.Publish(descriptors[i]);
                warmed++;
            }
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore CODY L2->L1 window warm: descriptors=" + warmed + "/" + descriptors.Length +
                ", bounds=" + worldBounds + ".");
            return warmed;
        }

        internal void PublishCodyCatchment(CodyCatchmentDescriptor descriptor)
        {
            if (_codyL1 == null || _codyL2 == null)
                throw new InvalidOperationException("CODY runtime is unavailable because its fingerprint was not valid at startup.");
            _codyL2.Publish(descriptor);
            if (_hasCodyWarmBounds && descriptor.DependencyBounds.Intersects(_codyWarmBounds))
                _codyL1.Publish(descriptor);
            if (!_codyL2ArtifactRejected) _codyPersistenceWritable = true;
        }

        internal bool RequestCodyCatchmentRebuild(
            CodyCatchmentDescriptor seed,
            Func<CodyCatchmentDescriptor> buildFromSnapshot)
        {
            if (_codyRebuilds == null)
                throw new InvalidOperationException("CODY runtime is unavailable because its fingerprint was not valid at startup.");
            return _codyRebuilds.Request(seed, buildFromSnapshot);
        }

        internal void QueueCodyCatchmentCoverage(VolumetricFiniteDomainController domain, Bounds worldBounds)
        {
            if (_codyRebuilds == null || domain == null || !domain.Initialized) return;
            _pendingCodyDomain = domain;
            _pendingCodyCoverageBounds = worldBounds;
            _pendingCodyGeometryRevision = domain.AppliedGeometryGeneration;
        }

        internal IReadOnlyList<ProbeColonyCausalGeometrySignal> LastDrainedCausalGeometrySignals =>
            _causalSignals.LastDrainedSignals;

        internal bool TryGetSourceRuntimeRecord(string sourceId, out SourceRuntimeRecord record) =>
            _sourceRuntimeRecords.TryGetValue(sourceId, out record);

        internal void MarkCausalGeometryApplied(
            IReadOnlyList<ProbeColonyCausalGeometrySignal> signals,
            long generation,
            Bounds sdfDependencyRegion,
            Bounds cutCellDependencyRegion,
            Bounds apertureDependencyRegion,
            Bounds gpuResidentRegion,
            int sdfCellsUpdated,
            int cutCellsUpdated,
            int apertureFacesUpdated,
            int gpuBytesPatched)
        {
            if (signals == null) return;
            for (int i = 0; i < signals.Count; i++)
            {
                ProbeColonyCausalGeometrySignal signal = signals[i];
                if (!_sourceRuntimeRecords.TryGetValue(signal.SourceId, out SourceRuntimeRecord record)) continue;
                if (signal.SourceRevision < record.LastAppliedLcRevision) continue;
                record.LastAppliedLcRevision = signal.SourceRevision;
                record.LastAppliedGeneration = generation;
                record.SdfDependencyRegion = sdfDependencyRegion;
                record.CutCellDependencyRegion = cutCellDependencyRegion;
                record.ApertureDependencyRegion = apertureDependencyRegion;
                record.GpuResidentRegion = gpuResidentRegion;
                record.GpuResidentRegionHandle = record.LcPreparedGeometryHandle;
                record.SdfCellsUpdated = sdfCellsUpdated;
                record.CutCellsUpdated = cutCellsUpdated;
                record.ApertureFacesUpdated = apertureFacesUpdated;
                record.GpuBytesPatched = gpuBytesPatched;
            }
        }

        private SourceRuntimeRecord ResolveSourceRuntimeRecord(ValheimPceGeometryChange change)
        {
            if (!_sourceRuntimeRecords.TryGetValue(change.SourceId, out SourceRuntimeRecord record))
            {
                int handle = ++_nextSourceRuntimeHandle;
                record = new SourceRuntimeRecord
                {
                    SourceId = change.SourceId,
                    PceOccupancyHandle = handle,
                    LcPreparedGeometryHandle = handle
                };
                _sourceRuntimeRecords.Add(change.SourceId, record);
            }

            record.AssetClassId = string.IsNullOrEmpty(change.AssetClassId) ? "unknown" : change.AssetClassId;
            record.AuthoritativeRevision = change.Revision;
            record.WorldBounds = change.HasNewWorldBounds ? change.NewWorldBounds : change.OldWorldBounds;
            record.PceTileMin = change.DirtyMin;
            record.PceTileMax = change.DirtyMax;
            record.DatabaseHit = change.DatabaseHit;
            record.PreparedGeometry = ResolvePreparedGeometry(change);
            if (change.Root != null)
            {
                Transform transform = change.Root.transform;
                record.Position = transform.position;
                record.Rotation = transform.rotation;
                record.Scale = transform.lossyScale;
            }
            return record;
        }

        private VolumetricPreparedGeometryDescriptor ResolvePreparedGeometry(ValheimPceGeometryChange change)
        {
            if (string.IsNullOrEmpty(change.AssetClassId) || change.ColliderRecipes == null ||
                change.ColliderRecipes.Length == 0) return null;
            string key = change.AssetClassId + "|" + (change.GeometrySignature ?? string.Empty);
            if (_preparedGeometryByAsset.TryGetValue(key, out VolumetricPreparedGeometryDescriptor cached))
                return cached;
            var addresses = new VolumetricPreparedColliderAddress[change.ColliderRecipes.Length];
            for (int i = 0; i < change.ColliderRecipes.Length; i++)
            {
                ValheimKnowledgeDatabase.ColliderRecipe recipe = change.ColliderRecipes[i];
                if (recipe == null || recipe.transformChildIndices == null ||
                    recipe.colliderComponentIndex < 0 || string.IsNullOrEmpty(recipe.colliderType))
                    return null;
                addresses[i] = new VolumetricPreparedColliderAddress
                {
                    TransformChildIndices = recipe.transformChildIndices,
                    ColliderComponentIndex = recipe.colliderComponentIndex,
                    ColliderType = recipe.colliderType,
                    MeshName = recipe.meshName,
                    MeshVertexCount = recipe.meshVertexCount,
                    MeshTriangleCount = recipe.meshTriangleCount
                };
            }
            cached = new VolumetricPreparedGeometryDescriptor
            {
                AssetClassId = change.AssetClassId,
                GeometrySignature = change.GeometrySignature,
                Colliders = addresses
            };
            _preparedGeometryByAsset.Add(key, cached);
            return cached;
        }

        internal bool TryConsumeCausalGeometrySignals(
            Bounds worldBounds,
            long generationAfter,
            List<GameObject> addedOrChangedRoots,
            List<int> removedRootInstanceIds,
            out ProbeColonyCausalGeometryBatch batch)
        {
            return _causalSignals.TryDrain(
                worldBounds,
                generationAfter,
                addedOrChangedRoots,
                removedRootInstanceIds,
                out batch);
        }

        internal void DiscardCausalGeometrySignalsThrough(long generation)
        {
            _causalSignals.DiscardThrough(generation);
        }

        internal bool TryGetReadyChangeBounds(
            long generation,
            out Bounds dirtyWorldBounds,
            out float eventToReadyMilliseconds,
            out float readyAgeMilliseconds)
        {
            dirtyWorldBounds = default;
            eventToReadyMilliseconds = 0f;
            readyAgeMilliseconds = 0f;
            return _adapter != null && _adapter.TryGetReadyDirtyWorldBounds(
                generation,
                out dirtyWorldBounds,
                out eventToReadyMilliseconds,
                out readyAgeMilliseconds);
        }

        internal int ApplyMappedColliderOccupancy(Bounds domainBounds, Vector3 domainOrigin, float cellSize)
        {
            if (cellSize <= 0f) return 0;
            int refinedSources = 0;
            int deferredLargeSources = 0;
            foreach (ValheimPceGeometryChange change in _activeSources.Values)
            {
                if (!change.CanFeedSdf || change.Root == null || !change.Root.activeInHierarchy ||
                    !domainBounds.Intersects(change.NewWorldBounds)) continue;
                IList<Collider> colliders;
                if (_sourceRuntimeRecords.TryGetValue(change.SourceId, out SourceRuntimeRecord record) &&
                    record.PreparedGeometry != null &&
                    record.PreparedGeometry.TryResolveColliders(
                        change.Root,
                        _mappedPreparedColliders,
                        _mappedColliderComponentScratch))
                    colliders = _mappedPreparedColliders;
                else
                    colliders = change.Root.GetComponentsInChildren<Collider>(true);
                if (colliders == null || colliders.Count == 0) continue;
                Bounds clipped = change.NewWorldBounds;
                clipped.Expand(0.5f * cellSize);
                clipped = IntersectBounds(clipped, domainBounds);
                Vector3Int min = WorldToCell(clipped.min, domainOrigin, cellSize);
                Vector3Int max = WorldToCell(clipped.max, domainOrigin, cellSize);
                long cellCount = (long)(max.x - min.x + 1) * (max.y - min.y + 1) * (max.z - min.z + 1);
                if (cellCount > 65536L)
                {
                    // Large terrain colliders must not turn the causal gate into
                    // an unbounded synchronous Physics.ClosestPoint scan. The
                    // authoritative source/SDF rebuild still covers this region;
                    // exact collider sampling remains enabled for bounded
                    // construction and dynamic-solid changes.
                    deferredLargeSources++;
                    continue;
                }
                var source = new VolumetricWorldGeometrySource
                {
                    SourceId = change.SourceId,
                    Category = ToPceCategory(change.Category),
                    RevisionHash = unchecked((int)change.Revision),
                    Root = change.Root,
                    WorldBounds = change.NewWorldBounds,
                    CanFeedSdf = change.CanFeedSdf
                };
                var region = new VolumetricWorldGeometryVoxelRegion { Min = min, Max = max, Valid = true };
                _geometry.ApplySourceAtRegion(source, region, cell => SampleColliderOccupancy(colliders, domainOrigin + (new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f) * cellSize), cellSize));
                refinedSources++;
            }
            if (refinedSources > 0)
                PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE mapped collider occupancy (solid/partial) for " + refinedSources + " source(s); dirty bounds remained only the update region.");
            if (deferredLargeSources > 0)
                PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE deferred exact collider occupancy for " + deferredLargeSources + " large source region(s) to the bounded source/SDF rebuild.");
            return refinedSources;
        }

        internal void PopulatePreparedGeometryByRoot(
            IReadOnlyList<GameObject> roots,
            Dictionary<int, VolumetricPreparedGeometryDescriptor> preparedGeometryByRoot)
        {
            if (preparedGeometryByRoot == null) throw new ArgumentNullException(nameof(preparedGeometryByRoot));
            preparedGeometryByRoot.Clear();
            _preparedDescriptorAmbiguousRoots.Clear();
            _snapshotAdapterRootIds.Clear();
            if (roots == null) return;
            for (int i = 0; i < roots.Count; i++)
                if (roots[i] != null) _snapshotAdapterRootIds.Add(roots[i].GetInstanceID());

            foreach (KeyValuePair<string, ValheimPceGeometryChange> pair in _activeSources)
            {
                ValheimPceGeometryChange source = pair.Value;
                if (source.Root == null) continue;
                int rootId = source.Root.GetInstanceID();
                if (!_snapshotAdapterRootIds.Contains(rootId) || _preparedDescriptorAmbiguousRoots.Contains(rootId)) continue;
                if (!_sourceRuntimeRecords.TryGetValue(pair.Key, out SourceRuntimeRecord record) || record.PreparedGeometry == null)
                {
                    preparedGeometryByRoot.Remove(rootId);
                    _preparedDescriptorAmbiguousRoots.Add(rootId);
                    continue;
                }
                if (preparedGeometryByRoot.TryGetValue(rootId, out VolumetricPreparedGeometryDescriptor existing) &&
                    !ReferenceEquals(existing, record.PreparedGeometry))
                {
                    preparedGeometryByRoot.Remove(rootId);
                    _preparedDescriptorAmbiguousRoots.Add(rootId);
                    continue;
                }
                preparedGeometryByRoot[rootId] = record.PreparedGeometry;
            }
        }

        private static ProbeOccupancy SampleColliderOccupancy(IList<Collider> colliders, Vector3 point, float cellSize)
        {
            const float tolerance = 0.0001f;
            Bounds cell = new Bounds(point, Vector3.one * cellSize);
            bool intersects = false;
            for (int i = 0; i < colliders.Count; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger || !collider.enabled || !collider.bounds.Intersects(cell)) continue;
                intersects = true;
                // Unity only supports ClosestPoint for primitive colliders and
                // convex MeshColliders. Non-convex mesh/compound colliders are
                // conservatively partial here; calling ClosestPoint on them
                // logs once per sampled cell and can freeze the live client.
                if (!(collider is BoxCollider) && !(collider is SphereCollider) &&
                    !(collider is CapsuleCollider) &&
                    (!(collider is MeshCollider mesh) || !mesh.convex))
                    continue;
                if ((collider.ClosestPoint(point) - point).sqrMagnitude <= tolerance) return ProbeOccupancy.Solid;
            }
            return intersects ? ProbeOccupancy.Partial : ProbeOccupancy.Open;
        }

        private static Vector3Int WorldToCell(Vector3 point, Vector3 origin, float cellSize)
        {
            return new Vector3Int(Mathf.FloorToInt((point.x - origin.x) / cellSize), Mathf.FloorToInt((point.y - origin.y) / cellSize), Mathf.FloorToInt((point.z - origin.z) / cellSize));
        }

        private static Bounds IntersectBounds(Bounds a, Bounds b)
        {
            Vector3 min = Vector3.Max(a.min, b.min);
            Vector3 max = Vector3.Min(a.max, b.max);
            return new Bounds((min + max) * 0.5f, Vector3.Max(Vector3.zero, max - min));
        }

        internal bool TryGetCausalGeometrySnapshot(
            Bounds worldBounds,
            long generationAfter,
            List<GameObject> roots,
            Dictionary<int, int> rootRevisions,
            out long generation,
            out int stateRevision)
        {
            generation = -1;
            stateRevision = 0;
            if (_adapter == null) return false;
            // Always retain the adapter's exact per-root revision digest locally,
            // even when the caller only requests the root list. PCE must prove
            // that its retained source records represent the same generation
            // before the SDF/cut-cell path consumes the routed roots.
            _snapshotAdapterRevisions.Clear();
            if (!_adapter.TryGetCausalGeometrySnapshot(worldBounds, generationAfter, roots, _snapshotAdapterRevisions, out generation, out stateRevision)) return false;

            _snapshotAdapterRootIds.Clear();
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root != null) _snapshotAdapterRootIds.Add(root.GetInstanceID());
            }

            _snapshotRepresentedRootIds.Clear();
            _snapshotPceRoots.Clear();
            _snapshotPceRevisions.Clear();
            foreach (ValheimPceGeometryChange source in _activeSources.Values)
            {
                GameObject root = source.Root;
                if (root == null) continue;
                int rootId = root.GetInstanceID();
                _snapshotRepresentedRootIds.Add(rootId);
                if (!root.activeInHierarchy || !source.CanFeedSdf ||
                    !worldBounds.Intersects(source.NewWorldBounds) ||
                    !_snapshotAdapterRootIds.Contains(rootId)) continue;
                if (!_snapshotPceRoots.ContainsKey(rootId)) _snapshotPceRoots.Add(rootId, root);
                if (!_causalRootRevisionDigests.ContainsKey(rootId))
                {
                    if (_snapshotPceRevisions.TryGetValue(rootId, out int previous))
                        _snapshotPceRevisions[rootId] = previous ^ unchecked((int)source.Revision);
                    else
                        _snapshotPceRevisions.Add(rootId, unchecked((int)source.Revision));
                }
            }
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root != null && _snapshotRepresentedRootIds.Contains(root.GetInstanceID())) continue;
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore PCE causal gate rejected an SDF snapshot because its root is not represented by an exact retained PCE source: " + (root != null ? root.name : "<null>") + ".");
                roots.Clear();
                rootRevisions?.Clear();
                return false;
            }

            // The adapter remains the accuracy authority for readiness and
            // generation, but the causal root/revision set consumed by the SDF
            // path must cross the PCE boundary. Restrict it to the exact roots
            // accepted by the adapter snapshot so newly observed events cannot
            // leak into a partially ready generation.
            if (_snapshotPceRoots.Count != _snapshotAdapterRootIds.Count)
            {
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore PCE causal gate rejected an SDF snapshot because the retained PCE root set was incomplete.");
                roots.Clear();
                rootRevisions?.Clear();
                return false;
            }
            foreach (KeyValuePair<int, int> adapterRevision in _snapshotAdapterRevisions)
            {
                _causalRootRevisionDigests[adapterRevision.Key] = adapterRevision.Value;
                if (rootRevisions != null) _snapshotPceRevisions[adapterRevision.Key] = adapterRevision.Value;
            }
            roots.Clear();
            foreach (GameObject root in _snapshotPceRoots.Values) roots.Add(root);
            if (rootRevisions != null)
            {
                rootRevisions.Clear();
                foreach (KeyValuePair<int, int> pair in _snapshotPceRevisions) rootRevisions.Add(pair.Key, pair.Value);
            }
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE supplied the causal SDF root set: roots=" + roots.Count + ", generation=" + generation + ", stateRevision=" + stateRevision + ".");
            return true;
        }

        private static VolumetricWorldGeometryCategory ToPceCategory(ValheimWorldGeometryCategory category)
        {
            switch (category)
            {
                case ValheimWorldGeometryCategory.Terrain: return VolumetricWorldGeometryCategory.Terrain;
                case ValheimWorldGeometryCategory.DynamicSolid: return VolumetricWorldGeometryCategory.DynamicSolid;
                case ValheimWorldGeometryCategory.NonSolidDecorative:
                case ValheimWorldGeometryCategory.Trigger:
                case ValheimWorldGeometryCategory.CharacterCreature:
                case ValheimWorldGeometryCategory.ItemDrop:
                case ValheimWorldGeometryCategory.VehicleShip:
                case ValheimWorldGeometryCategory.Vegetation:
                case ValheimWorldGeometryCategory.ParticleOrEffect:
                case ValheimWorldGeometryCategory.WaterVolume: return VolumetricWorldGeometryCategory.ParticleOrEffect;
                default: return VolumetricWorldGeometryCategory.SolidBarrier;
            }
        }

        private void OnDestroy()
        {
            FlushCody();
            if (_adapter != null) _adapter.PceGeometryChanged -= OnGeometryChanged;
            if (Instance == this) Instance = null;
        }

        private void InitializeCody()
        {
            ValheimKnowledgeDatabase knowledge = PhysicalWaterPlugin.ValheimKnowledge;
            if (knowledge == null || !knowledge.Loaded || knowledge.Data == null ||
                knowledge.Data.valheim == null || knowledge.Data.modSet == null)
            {
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore CODY catchment cache disabled because the Valheim knowledge fingerprint is unavailable.");
                return;
            }
            string gameFingerprint = knowledge.Data.valheim.assemblySha256;
            string modFingerprint = knowledge.Data.modSet.fingerprint;
            int schemaVersion = CodyCatchmentSchemaVersion;
            _codyL1 = new CodyCatchmentCache(gameFingerprint, modFingerprint, schemaVersion, CodyRegionWorldSize);
            _codyL2 = new CodyCatchmentCache(gameFingerprint, modFingerprint, schemaVersion, CodyRegionWorldSize);
            _codyRebuilds = new CodyCatchmentRebuildCoordinator(_codyL2);
            _codyRebuilds.Published += OnCodyCatchmentRebuilt;
            _codyL2Path = Path.Combine(Paths.ConfigPath, "LiquidCore",
                "cody-catchments-v" + schemaVersion + ".bin");
            _codyInitialWorldSourceRelationPath = Path.Combine(Paths.ConfigPath, "LiquidCore",
                "cody-initial-world-source-v1.bin");
            try
            {
                int loaded = CodyCatchmentL2Store.Load(_codyL2Path, _codyL2);
                _codyPersistenceWritable = true;
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore CODY catchment cache ready: game=" + gameFingerprint +
                    ", mods=" + modFingerprint + ", schema=" + schemaVersion +
                    ", region=" + CodyRegionWorldSize + "m, L2=" + loaded + ", L1=0.");
            }
            catch (Exception ex)
            {
                // Fail closed and preserve the rejected artifact for diagnosis.
                // Do not overwrite it with an empty cache on shutdown.
                _codyPersistenceWritable = false;
                _codyL2ArtifactRejected = true;
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore CODY rejected its L2 cache and will keep an empty L1: " + ex.Message);
            }
            // The semantic source relation is an independent CODY artifact;
            // its replay must not be suppressed by an unrelated L2 cache
            // deserialization failure. Final PCE closure still validates its
            // world, revision, and membership contract before attachment.
            try
            {
                CodyInitialWorldSourceRelation relation =
                    CodyInitialWorldSourceRelationStore.Load(_codyInitialWorldSourceRelationPath);
                if (relation != null && !_codyL2.TryPublishInitialWorldSourceRelation(relation, out string relationError))
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore rejected persisted CODY initial-world source relation: " + relationError + ".");
            }
            catch (Exception relationException)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore rejected persisted CODY initial-world source relation: " + relationException.Message + ".");
            }
        }

        private void OnCodyCatchmentRebuilt(CodyCatchmentDescriptor descriptor)
        {
            if (_codyL1 != null && _hasCodyWarmBounds && descriptor.DependencyBounds.Intersects(_codyWarmBounds))
                _codyL1.Publish(descriptor);
            if (!_codyL2ArtifactRejected) _codyPersistenceWritable = true;
            if (descriptor != null &&
                TryGetCodyInitialWaterSourceSeed(out CodyCatchmentDescriptor sourceSeed, out _) &&
                sourceSeed.CatchmentId == descriptor.CatchmentId)
            {
                _baseWorldPceBootstrapJob = null;
                _baseWorldBootstrapWorldKey = null;
                _baseWorldBootstrapDomainFingerprint = null;
                _baseWorldBootstrapAttemptKey = null;
                _baseWorldBootstrapFailureReported = false;
            }
            CodyCatchmentPublished?.Invoke(descriptor);
            CodyCatchmentRebuildCoordinatorDiagnostics diagnostics = _codyRebuilds.Diagnostics;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore CODY background rebuild published: catchment=" + descriptor.CatchmentId +
                ", region=(" + descriptor.RegionX + "," + descriptor.RegionZ + ")" +
                ", requests/dispatch/coalesced=" + diagnostics.Requests + "/" + diagnostics.Dispatches + "/" + diagnostics.CoalescedRequests +
                ", published/stale/failed=" + diagnostics.Published + "/" + diagnostics.StaleCompletionsRejected + "/" + diagnostics.Failed + ".");
        }

        private void DispatchPendingCodyCoverage()
        {
            VolumetricFiniteDomainController domain = _pendingCodyDomain;
            if (_codyRebuilds == null || domain == null || !domain.Initialized || _pendingCodyGeometryRevision < 0) return;
            // Keep only the latest queued coverage request while an older
            // bounded batch is running. Once its completions publish/reject,
            // the retained request snapshots current geometry exactly once.
            if (_codyRebuilds.PendingCount > 0) return;
            Bounds requestedBounds = _pendingCodyCoverageBounds;
            long geometryRevision = _pendingCodyGeometryRevision;
            _pendingCodyDomain = null;
            _pendingCodyGeometryRevision = -1;

            Bounds represented = IntersectBounds(requestedBounds, domain.WorldBounds);
            if (represented.size.x <= 0f || represented.size.z <= 0f) return;
            VolumetricWaterSettings settings = domain.MacDomain.Settings;
            byte[] cutU = domain.MacDomain.CaptureCutCellUQuantizedSync();
            byte[] cutW = domain.MacDomain.CaptureCutCellWQuantizedSync();
            int minimumRegionX = Mathf.FloorToInt(represented.min.x / CodyRegionWorldSize);
            int maximumRegionX = Mathf.FloorToInt((represented.max.x - 1e-4f) / CodyRegionWorldSize);
            int minimumRegionZ = Mathf.FloorToInt(represented.min.z / CodyRegionWorldSize);
            int maximumRegionZ = Mathf.FloorToInt((represented.max.z - 1e-4f) / CodyRegionWorldSize);
            int requested = 0;
            for (int regionZ = minimumRegionZ; regionZ <= maximumRegionZ; regionZ++)
            for (int regionX = minimumRegionX; regionX <= maximumRegionX; regionX++)
            {
                Vector3 regionCenter = new Vector3(
                    (regionX + 0.5f) * CodyRegionWorldSize,
                    represented.center.y,
                    (regionZ + 0.5f) * CodyRegionWorldSize);
                if (_codyL1.TryLookup(regionCenter, out _)) continue;
                Bounds dependencyBounds = new Bounds(regionCenter,
                    new Vector3(CodyRegionWorldSize, represented.size.y, CodyRegionWorldSize));
                dependencyBounds = IntersectBounds(dependencyBounds, domain.WorldBounds);
                var dependencies = new List<CodyCatchmentDependency>();
                foreach (KeyValuePair<string, SourceRuntimeRecord> item in _sourceRuntimeRecords)
                {
                    if (!_activeSources.ContainsKey(item.Key) || !item.Value.WorldBounds.Intersects(dependencyBounds)) continue;
                    dependencies.Add(new CodyCatchmentDependency
                    {
                        SourceId = item.Key,
                        Revision = item.Value.AuthoritativeRevision,
                        WorldBounds = item.Value.WorldBounds
                    });
                }
                dependencies.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));
                var snapshot = new CodyCatchmentBuildSnapshot
                {
                    GameBuildFingerprint = _codyL2.GameBuildFingerprint,
                    ModFingerprint = _codyL2.ModFingerprint,
                    SchemaVersion = _codyL2.SchemaVersion,
                    RegionX = regionX,
                    RegionZ = regionZ,
                    RegionWorldSize = CodyRegionWorldSize,
                    GeometryRecipeRevision = geometryRevision,
                    ValidatedUtcTicks = DateTime.UtcNow.Ticks,
                    DependencyBounds = dependencyBounds,
                    Dependencies = dependencies.ToArray(),
                    GridWorldOrigin = domain.WorldOrigin,
                    CellSize = settings.CellSize,
                    ResolutionX = settings.ResolutionX,
                    ResolutionY = settings.ResolutionY,
                    ResolutionZ = settings.ResolutionZ,
                    CutU = cutU,
                    CutW = cutW
                };
                CodyCatchmentDescriptor seed = CodyCatchmentBuilder.CreateSeed(snapshot);
                if (_codyRebuilds.Request(seed, () => CodyCatchmentBuilder.Build(snapshot))) requested++;
            }
            if (requested > 0)
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore CODY queued " + requested + " bounded catchment rebuild(s) from geometry revision " +
                    geometryRevision + "; cut-cell snapshot was deferred beyond the causal terrain-apply frame.");
        }

        private void ApplyCodyDependencyChange(ValheimPceGeometryChange change)
        {
            if (_codyL1 == null || _codyL2 == null || string.IsNullOrWhiteSpace(change.SourceId)) return;
            Bounds dirty = change.HasNewWorldBounds ? change.NewWorldBounds : change.OldWorldBounds;
            if (change.HasOldWorldBounds && change.HasNewWorldBounds) dirty.Encapsulate(change.OldWorldBounds);
            _codyInvalidated.Clear();
            int l2Invalidated = _codyL2.ApplyDependencyChange(change.SourceId, change.Revision, dirty, _codyInvalidated);
            int l1Invalidated = _codyL1.ApplyDependencyChange(change.SourceId, change.Revision, dirty);
            RearmConfiguredSourceMarkerIfAffected(_codyInvalidated);
            if (l2Invalidated == 0 && l1Invalidated == 0) return;
            if (!_codyL2ArtifactRejected) _codyPersistenceWritable = true;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore DNA invalidated CODY catchments directly: source=" + change.SourceId +
                ", revision=" + change.Revision + ", L1/L2=" + l1Invalidated + "/" + l2Invalidated +
                ", affected=" + string.Join(",", _codyInvalidated.ConvertAll(id => id.ToString()).ToArray()) + ".");
        }

        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private void FlushCody()
        {
            if (!_codyPersistenceWritable || _codyL2 == null || string.IsNullOrEmpty(_codyL2Path)) return;
            try
            {
                int saved = CodyCatchmentL2Store.Save(_codyL2Path, _codyL2);
                PhysicalWaterPlugin.Log.LogInfo("LiquidCore CODY L2 persisted " + saved + " valid catchment descriptors.");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore CODY could not persist its L2 cache: " + ex.Message);
            }
        }
    }
}
