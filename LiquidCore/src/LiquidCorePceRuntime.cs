using System;
using System.Collections.Generic;
using System.IO;
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
        private const int CodyCatchmentSchemaVersion = 2;
        private CodyCatchmentCache _codyL1;
        private CodyCatchmentCache _codyL2;
        private CodyCatchmentRebuildCoordinator _codyRebuilds;
        private string _codyL2Path;
        private bool _codyPersistenceWritable;
        private bool _codyL2ArtifactRejected;
        private bool _hasCodyWarmBounds;
        private Bounds _codyWarmBounds;
        private readonly List<ulong> _codyInvalidated = new List<ulong>();
        private VolumetricFiniteDomainController _pendingCodyDomain;
        private Bounds _pendingCodyCoverageBounds;
        private long _pendingCodyGeometryRevision = -1;
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
            for (int i = 0; i < closed.Length; i++)
                if (!PublishCapacityStorage(closed[i], out error))
                {
                    domain = null;
                    return false;
                }
            return PublishCompleteInitialWorldDomain(domain, out error);
        }

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
        }

        private void OnCodyCatchmentRebuilt(CodyCatchmentDescriptor descriptor)
        {
            if (_codyL1 != null && _hasCodyWarmBounds && descriptor.DependencyBounds.Intersects(_codyWarmBounds))
                _codyL1.Publish(descriptor);
            if (!_codyL2ArtifactRejected) _codyPersistenceWritable = true;
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
            if (l2Invalidated == 0 && l1Invalidated == 0) return;
            if (!_codyL2ArtifactRejected) _codyPersistenceWritable = true;
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore DNA invalidated CODY catchments directly: source=" + change.SourceId +
                ", revision=" + change.Revision + ", L1/L2=" + l1Invalidated + "/" + l2Invalidated +
                ", affected=" + string.Join(",", _codyInvalidated.ConvertAll(id => id.ToString()).ToArray()) + ".");
        }

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
