using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal enum ValheimWorldGeometryCategory
    {
        SolidBarrier,
        ThinBlockingBarrier,
        Terrain,
        DynamicSolid,
        NonSolidDecorative,
        Trigger,
        CharacterCreature,
        ItemDrop,
        VehicleShip,
        Vegetation,
        ParticleOrEffect,
        WaterVolume,
        Unsupported
    }

    internal enum ValheimWorldGeometryKind
    {
        BoxCollider,
        SphereCollider,
        CapsuleCollider,
        MeshCollider,
        CompoundColliderHierarchy,
        HeightmapCollisionMesh,
        Unsupported
    }

    internal enum ValheimWorldGeometryChangeKind
    {
        Added,
        Removed,
        MovedOrChanged
    }

    internal struct ValheimPceGeometryChange
    {
        internal string SourceId;
        internal ValheimWorldGeometryCategory Category;
        internal ValheimWorldGeometryChangeKind ChangeKind;
        internal Bounds OldWorldBounds;
        internal Bounds NewWorldBounds;
        internal Vector3Int DirtyMin;
        internal Vector3Int DirtyMax;
        internal uint Revision;
        internal GameObject Root;
        internal ValheimWorldGeometryKind Kind;
        internal string HierarchyPath;
        internal string RootType;
        internal bool CanFeedSdf;
    }

    internal sealed class PhysicalWaterValheimWorldGeometryAdapter : MonoBehaviour
    {
        internal event Action<ValheimPceGeometryChange> PceGeometryChanged;
        private sealed class Source
        {
            internal string Id;
            internal string Path;
            internal string RootType;
            internal ValheimWorldGeometryCategory Category;
            internal ValheimWorldGeometryKind Kind;
            internal Bounds Bounds;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
            internal int CheapRevision;
            internal int Revision;
            internal int Colliders;
            internal int MeshColliders;
            internal int PrimitiveColliders;
            internal int TriggerColliders;
            internal int MeshVertices;
            internal int MeshTriangles;
            internal bool NonUniformScale;
            internal bool CanFeedSdf;
            internal string RejectionReason;
            internal GameObject Root;
        }

        private sealed class CachedSource
        {
            internal Source Source;
            internal bool Seen;
            internal bool ExplicitlyDirty;
            internal HashSet<ChunkKey> Chunks = new HashSet<ChunkKey>();
            internal HashSet<ChunkKey> DiscoveryChunks = new HashSet<ChunkKey>();
        }

        private sealed class DiscoveryChunk
        {
            internal readonly HashSet<string> SourceIds = new HashSet<string>();
            internal int Revision;
            internal float LastDiscoveryTime;
        }

        private sealed class EventRecord
        {
            internal int Sequence;
            internal string Reason;
            internal string SourceId;
            internal string SourceType;
            internal string Path;
            internal ValheimWorldGeometryCategory Category;
            internal Bounds OldBounds;
            internal Bounds NewBounds;
            internal bool HasOldBounds;
            internal bool HasNewBounds;
            internal float Time;
            internal GameObject Root;
        }

        private sealed class DeferredEvent
        {
            internal Component Source;
            internal float DueTime;
        }

        private struct TargetedEventReport
        {
            internal bool Applied;
            internal string Outcome;
            internal int QueuedJobs;
            internal double ClassificationMilliseconds;
            internal double RevisionHashMilliseconds;
            internal double DirtyDetectionMilliseconds;
        }

        private sealed class ChunkState
        {
            internal int Revision;
            internal int SourceMemberships;
            internal int LastDirtyCells;
            internal bool Pending;
        }

        private sealed class DirtyChunkJob
        {
            internal int BatchId;
            internal int Generation;
            internal ChunkKey Key;
            internal VoxelRegion Region;
            internal string Reason;
            internal string SourceId;
            internal string SourceType;
            internal ValheimWorldGeometryChangeKind ChangeKind;
            internal int SourceMemberships;
            internal float QueuedTime;
            internal double OriginalOccupancyMs;
            internal double OriginalSdfMs;
            internal double OriginalUploadMs;
            internal double OccupancyRemainingMs;
            internal double SdfRemainingMs;
            internal double UploadRemainingMs;
            internal bool Started;

            internal bool Complete => OccupancyRemainingMs <= 0.0001 && SdfRemainingMs <= 0.0001 && UploadRemainingMs <= 0.0001;

            internal double Consume(double budget, out string stage)
            {
                stage = "none";
                if (budget <= 0.0001) return 0.0;
                Started = true;
                if (OccupancyRemainingMs > 0.0001)
                {
                    stage = "occupancy";
                    double used = Math.Min(budget, OccupancyRemainingMs);
                    OccupancyRemainingMs -= used;
                    return used;
                }
                if (SdfRemainingMs > 0.0001)
                {
                    stage = "sdf";
                    double used = Math.Min(budget, SdfRemainingMs);
                    SdfRemainingMs -= used;
                    return used;
                }
                if (UploadRemainingMs > 0.0001)
                {
                    stage = "upload";
                    double used = Math.Min(budget, UploadRemainingMs);
                    UploadRemainingMs -= used;
                    return used;
                }
                return 0.0;
            }
        }

        private readonly Dictionary<string, CachedSource> _cache = new Dictionary<string, CachedSource>();
        private readonly Dictionary<string, EventRecord> _eventBySource = new Dictionary<string, EventRecord>();
        private readonly Dictionary<int, Source> _scanRoots = new Dictionary<int, Source>();
        private readonly Dictionary<ChunkKey, ChunkState> _chunkCache = new Dictionary<ChunkKey, ChunkState>();
        private readonly Dictionary<ChunkKey, DiscoveryChunk> _discoveryCache = new Dictionary<ChunkKey, DiscoveryChunk>();
        private readonly HashSet<ChunkKey> _activeChunks = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _nextChunks = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _chunkScratch = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _dirtyDiscoveryChunks = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _discoveryChunks = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _pendingDiscoveryChunks = new HashSet<ChunkKey>();
        private readonly HashSet<ChunkKey> _priorityDiscoveryChunks = new HashSet<ChunkKey>();
        private readonly ProbeColonyDiscoverySchedule<ChunkKey> _discoverySchedule = new ProbeColonyDiscoverySchedule<ChunkKey>();
        private readonly Dictionary<string, DeferredEvent> _deferredEvents = new Dictionary<string, DeferredEvent>();
        private readonly List<string> _dueDeferredEventIds = new List<string>();
        private readonly List<ChunkKey> _loadedChunks = new List<ChunkKey>();
        private readonly List<ChunkKey> _evictedChunks = new List<ChunkKey>();
        private readonly List<string> _removed = new List<string>();
        private readonly List<EventRecord> _events = new List<EventRecord>();
        private readonly Queue<DirtyChunkJob> _workQueue = new Queue<DirtyChunkJob>();
        private readonly Dictionary<ChunkKey, DirtyChunkJob> _pendingJobByChunk = new Dictionary<ChunkKey, DirtyChunkJob>();
        private readonly List<Heightmap> _heightmapsScratch = new List<Heightmap>();
        private readonly Collider[] _colliderScratch = new Collider[8192];
        private Vector3 _streamCenter = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
        private float _nextScanTime;
        private float _nextConsistencyScanTime;
        private int _scanIndex;
        private int _eventSequence;
        private bool _applicationQuitting;
        private int _queueBatchSequence;
        private int _queueBatchQueuedJobs;
        private int _queueBatchCompletedJobs;
        private int _queueBatchDirtyCells;
        private int _queueBatchFrames;
        private int _queueBatchOverruns;
        private int _queueBatchStaleDiscarded;
        private int _queueBatchCoalesced;
        private int _queueWorstDepth;
        private int _queueTotalStaleDiscarded;
        private int _queueTotalCoalesced;
        private float _queueBatchStartTime;
        private double _queueBatchOccupancyMs;
        private double _queueBatchSdfMs;
        private double _queueBatchUploadMs;
        private double _queueBatchWorstFrameMs;
        private int _lastColliderOverlapCount;
        private bool _lastColliderOverlapOverflow;
        private bool _forceScanAfterEvent;
        private double _lastQueueActiveCpuMs;
        private long _geometryGeneration;
        private long _readyGeometryGeneration;
        private Bounds _pendingDirtyWorldBounds;
        private bool _hasPendingDirtyWorldBounds;
        private Bounds _readyDirtyWorldBounds;
        private bool _hasReadyDirtyWorldBounds;
        private long _readyDirtyBoundsGeneration = -1;
        private Bounds _recentTerrainOperationBounds;
        private float _recentTerrainOperationTime = float.NegativeInfinity;

        // devE3.1: explicit causal-coverage requests may extend beyond the normal
        // player-centered D6 discovery window. While such a request is pending,
        // temporarily include its bounds in the effective discovery window so
        // priority tiles cannot become permanently unschedulable.
        private Bounds _causalCoverageBounds;

        private static readonly MethodInfo HeightmapGetWorldHeightMethod = typeof(Heightmap).GetMethod(
            "GetWorldHeight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Vector3), typeof(float).MakeByRefType() },
            null);

        private static readonly FieldInfo HeightmapColliderField = typeof(Heightmap).GetField(
            "m_collider",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo HeightmapMeshRendererField = typeof(Heightmap).GetField(
            "m_meshRenderer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo HeightmapMeshFilterField = typeof(Heightmap).GetField(
            "m_meshFilter",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo HeightmapWidthField = typeof(Heightmap).GetField(
            "m_width",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo HeightmapScaleField = typeof(Heightmap).GetField(
            "m_scale",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo HeightmapHeightsField = typeof(Heightmap).GetField(
            "m_heights",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static PhysicalWaterValheimWorldGeometryAdapter Instance { get; private set; }

        internal bool TryCaptureTerrainFixture(
            Bounds worldBounds,
            float requestedSpacing,
            string captureId,
            out VolumetricTerrainFixture fixture,
            out string error)
        {
            fixture = null;
            error = string.Empty;
            float spacing = Mathf.Clamp(requestedSpacing, 0.25f, 4f);
            int samplesX = Mathf.Clamp(Mathf.CeilToInt(worldBounds.size.x / spacing) + 1, 2, 513);
            int samplesZ = Mathf.Clamp(Mathf.CeilToInt(worldBounds.size.z / spacing) + 1, 2, 513);
            Vector3 origin = new Vector3(worldBounds.min.x, 0f, worldBounds.min.z);
            var heights = new float[samplesX * samplesZ];
            var sourceIds = new HashSet<string>();

            _heightmapsScratch.Clear();
            try
            {
                Heightmap.FindHeightmap(worldBounds.center, Mathf.Max(worldBounds.extents.x, worldBounds.extents.z) + spacing, _heightmapsScratch);
            }
            catch
            {
                List<Heightmap> all = Heightmap.GetAllHeightmaps();
                if (all != null) _heightmapsScratch.AddRange(all);
            }

            int missing = 0;
            for (int z = 0; z < samplesZ; z++)
            for (int x = 0; x < samplesX; x++)
            {
                float worldX = origin.x + x * spacing;
                float worldZ = origin.z + z * spacing;
                float selectedHeight = float.NegativeInfinity;
                bool found = false;
                for (int i = 0; i < _heightmapsScratch.Count; i++)
                {
                    Heightmap heightmap = _heightmapsScratch[i];
                    if (heightmap == null || !heightmap.gameObject.activeInHierarchy) continue;
                    Bounds heightmapBounds = GetHeightmapWorldBounds(heightmap);
                    if (worldX < heightmapBounds.min.x - 0.01f || worldX > heightmapBounds.max.x + 0.01f ||
                        worldZ < heightmapBounds.min.z - 0.01f || worldZ > heightmapBounds.max.z + 0.01f) continue;
                    if (!TryGetHeightmapWorldHeight(heightmap, new Vector3(worldX, worldBounds.center.y, worldZ), out float candidate)) continue;
                    if (!found || candidate > selectedHeight) selectedHeight = candidate;
                    found = true;
                    sourceIds.Add(BuildSourceId(heightmap.gameObject));
                }
                if (!found && Heightmap.GetHeight(new Vector3(worldX, worldBounds.center.y, worldZ), out float fallback))
                {
                    selectedHeight = fallback;
                    found = true;
                }
                if (!found)
                {
                    missing++;
                    selectedHeight = worldBounds.min.y;
                }
                heights[x + samplesX * z] = selectedHeight;
            }

            if (missing != 0)
            {
                error = "Terrain capture has " + missing + " uncovered samples out of " + heights.Length + ".";
                return false;
            }

            fixture = new VolumetricTerrainFixture
            {
                CaptureId = string.IsNullOrWhiteSpace(captureId) ? "valheim-terrain" : captureId.Trim(),
                Source = "Valheim Heightmap.GetWorldHeight",
                CapturedUtc = DateTime.UtcNow.ToString("O"),
                WorldOrigin = origin,
                SampleSpacing = spacing,
                SamplesX = samplesX,
                SamplesZ = samplesZ,
                Heights = heights,
                SourceIds = new List<string>(sourceIds).ToArray()
            };
            fixture.RebuildNormals();
            return fixture.Validate(out error);
        }

        internal bool TryGetCausalGeometrySnapshot(
            Bounds worldBounds,
            long generationAfter,
            List<GameObject> roots,
            out long generation,
            out int stateRevision)
        {
            return TryGetCausalGeometrySnapshot(worldBounds, generationAfter, roots, null, out generation, out stateRevision);
        }

        internal bool TryGetCausalGeometrySnapshot(
            Bounds worldBounds,
            long generationAfter,
            List<GameObject> roots,
            Dictionary<int, int> rootRevisions,
            out long generation,
            out int stateRevision)
        {
            generation = _readyGeometryGeneration;
            stateRevision = 0;
            if (roots == null || _workQueue.Count != 0 || generation <= generationAfter) return false;

            roots.Clear();
            rootRevisions?.Clear();
            var seen = new HashSet<int>();
            int sourceCount = 0;
            foreach (var kv in _cache)
            {
                Source source = kv.Value.Source;
                if (source == null || !source.CanFeedSdf || source.Root == null ||
                    !source.Root.activeInHierarchy || !worldBounds.Intersects(source.Bounds)) continue;
                unchecked
                {
                    stateRevision ^= (source.Id.GetHashCode() * 397) ^ source.Revision;
                    sourceCount++;
                }
                int rootId = source.Root.GetInstanceID();
                if (seen.Add(rootId)) roots.Add(source.Root);
                if (rootRevisions != null)
                {
                    if (rootRevisions.TryGetValue(rootId, out int previous)) rootRevisions[rootId] = previous ^ source.Revision;
                    else rootRevisions.Add(rootId, source.Revision);
                }
            }
            unchecked { stateRevision ^= sourceCount * 486187739; }
            return true;
        }

        internal void RequestCausalGeometryCoverage(Bounds worldBounds)
        {
            _causalCoverageBounds = worldBounds;
            _discoverySchedule.BeginCausalCoverageWindow();

            float tileSize = Mathf.Max(4f, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryTileSize.Value);
            _chunkScratch.Clear();
            AddWorldChunks(worldBounds, tileSize, _chunkScratch);
            foreach (ChunkKey key in _chunkScratch)
            {
                _priorityDiscoveryChunks.Add(key);
                _dirtyDiscoveryChunks.Add(key);
                _pendingDiscoveryChunks.Add(key);
                _discoverySchedule.Request(key);
            }
            _nextScanTime = 0f;

            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 causal geometry coverage request registered: bounds=" + worldBounds +
                ", priorityTiles=" + _priorityDiscoveryChunks.Count + ".");
        }

        internal bool CausalGeometryCoverageReady =>
            _priorityDiscoveryChunks.Count == 0 &&
            _discoverySchedule.RequiredCount == 0 &&
            _workQueue.Count == 0 &&
            !_discoverySchedule.ConsistencySweepActive;
        internal int CausalGeometryCoveragePendingChunks => _priorityDiscoveryChunks.Count;

        internal void ReleaseCausalGeometryCoverage()
        {
            _discoverySchedule.ReleaseCausalGate();
            _priorityDiscoveryChunks.Clear();
        }

        internal bool TryGetReadyDirtyWorldBounds(long generation, out Bounds dirtyWorldBounds)
        {
            dirtyWorldBounds = _readyDirtyWorldBounds;
            return _hasReadyDirtyWorldBounds && _readyDirtyBoundsGeneration == generation;
        }

        private void Awake()
        {
            Instance = this;
            LiquidCorePceRuntime existingPce = LiquidCorePceRuntime.Instance;
            if (existingPce != null) existingPce.Attach(this);
            _nextScanTime = 0f;
            _nextConsistencyScanTime = 0f;
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater Valheim geometry adapter created. It reuses cached discovery chunks, profiles scan stages, preserves the causal geometry queue, and supplies Stage E finite domains when explicitly enabled.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void Update()
        {
            if (PhysicalWaterPlugin.Settings == null ||
                (!PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value &&
                 !PhysicalWaterPlugin.Settings.StageE1Enabled.Value))
            {
                return;
            }

            ProcessDeferredEvents();
            ProcessQueuedWork();

            float interval = Mathf.Max(0.25f, PhysicalWaterPlugin.Settings.ValheimGeometryScanInterval.Value);
            float now = Time.realtimeSinceStartup;
            bool finiteCoverageMode = PhysicalWaterPlugin.Settings.StageE1Enabled.Value && _discoverySchedule.CoverageWindowActive;
            bool waitingForFirstFiniteCoverage = PhysicalWaterPlugin.Settings.StageE1Enabled.Value && !_discoverySchedule.CoverageWindowActive;
            if (waitingForFirstFiniteCoverage &&
                _workQueue.Count == 0 &&
                _dirtyDiscoveryChunks.Count == 0 &&
                !_forceScanAfterEvent)
            {
                _pendingDiscoveryChunks.Clear();
                _discoverySchedule.TrySleep(false);
                return;
            }
            bool liveCoverageCanSleep =
                finiteCoverageMode &&
                _priorityDiscoveryChunks.Count == 0 &&
                _discoverySchedule.RequiredCount == 0 &&
                _workQueue.Count == 0 &&
                !_forceScanAfterEvent &&
                _dirtyDiscoveryChunks.Count == 0 &&
                now < _nextConsistencyScanTime;
            bool wasSleeping = _discoverySchedule.State == ProbeColonyChunkState.Sleeping;
            bool completingConsistency = _discoverySchedule.ConsistencySweepActive &&
                                         _discoverySchedule.RequiredCount == 0 &&
                                         _workQueue.Count == 0;
            if (liveCoverageCanSleep && _discoverySchedule.TrySleep(false))
            {
                // The explicit causal window is complete. Discard remaining
                // speculative discovery tiles and sleep until a Valheim event
                // or the bounded consistency interval wakes the adapter.
                // Without this boundary, background tile churn repeatedly
                // changes the global generation and forces full E3 SDF uploads.
                _pendingDiscoveryChunks.Clear();
                if (completingConsistency)
                {
                    PhysicalWaterPlugin.Log.LogInfo(
                        "LiquidCore PCE bounded consistency sweep complete; causal geometry is stable and returned to sleep.");
                }
                if (!wasSleeping)
                {
                    PhysicalWaterPlugin.Log.LogInfo(
                        "LiquidCore PCE geometry discovery sleeping: causal coverage complete, no dirty event work, " +
                        "requiredTiles=" + _discoverySchedule.RequiredCount +
                        ", eventWakeups=" + _discoverySchedule.EventWakeups +
                        ", coalescedRequired=" + _discoverySchedule.CoalescedWork +
                        ", " +
                        "next bounded consistency sweep in " +
                        Mathf.Max(0f, _nextConsistencyScanTime - now).ToString("F1") + "s.");
                }
                return;
            }
            if (wasSleeping)
            {
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore PCE geometry discovery waking: " +
                    (_forceScanAfterEvent || _dirtyDiscoveryChunks.Count > 0 || _discoverySchedule.RequiredCount > 0
                        ? "dirty Valheim event"
                        : "bounded consistency sweep") + ".");
            }
            if (!_forceScanAfterEvent && now < _nextScanTime)
            {
                return;
            }

            _forceScanAfterEvent = false;
            _nextScanTime = now + interval;
            ScanAndLog();
        }

        internal void MarkAllDirty()
        {
            foreach (var kv in _cache)
            {
                kv.Value.ExplicitlyDirty = true;
            }
            foreach (ChunkKey key in _activeChunks)
            {
                _dirtyDiscoveryChunks.Add(key);
                _discoverySchedule.Request(key);
            }
            _forceScanAfterEvent = true;
            _nextScanTime = 0f;
        }

        internal void MarkDirtyFromValheimEvent(string label, Component source)
        {
            MarkDirtyFromValheimEvent(label, source, true, null);
        }

        internal void MarkDirtyFromValheimEvent(string label, Component source, Bounds explicitDirtyWorldBounds)
        {
            _recentTerrainOperationBounds = explicitDirtyWorldBounds;
            _recentTerrainOperationTime = Time.realtimeSinceStartup;
            MarkDirtyFromValheimEvent(label, source, true, explicitDirtyWorldBounds);
        }

        private void MarkDirtyFromValheimEvent(string label, Component source, bool scheduleDoorFollowup, Bounds? explicitDirtyWorldBounds)
        {
            if (_applicationQuitting) return;

            Stopwatch eventWatch = Stopwatch.StartNew();
            EventRecord record = CaptureEvent(label, source);
            if (!explicitDirtyWorldBounds.HasValue && label.StartsWith("heightmap", StringComparison.OrdinalIgnoreCase) &&
                Time.realtimeSinceStartup - _recentTerrainOperationTime <= 2f)
                explicitDirtyWorldBounds = _recentTerrainOperationBounds;
            if (explicitDirtyWorldBounds.HasValue)
            {
                record.OldBounds = explicitDirtyWorldBounds.Value;
                record.NewBounds = explicitDirtyWorldBounds.Value;
                record.HasOldBounds = true;
                record.HasNewBounds = true;
            }
            bool destructive = IsDestructiveEvent(record.Reason);
            bool hadCachedSource = !string.IsNullOrEmpty(record.SourceId) && _cache.ContainsKey(record.SourceId);
            if (!CanFeedSdf(record.Category)) return;
            if (record.HasOldBounds) AccumulatePendingDirtyWorldBounds(record.OldBounds);
            if (record.HasNewBounds) AccumulatePendingDirtyWorldBounds(record.NewBounds);
            _events.Add(record);
            if (!string.IsNullOrEmpty(record.SourceId))
            {
                _eventBySource[record.SourceId] = record;
                CachedSource cached;
                if (_cache.TryGetValue(record.SourceId, out cached))
                {
                    cached.ExplicitlyDirty = true;
                }
            }

            float sdfChunkSize = PhysicalWaterPlugin.Settings != null
                ? Mathf.Max(8f, PhysicalWaterPlugin.Settings.ValheimGeometryChunkSize.Value)
                : 32f;
            float discoveryTileSize = PhysicalWaterPlugin.Settings != null
                ? Mathf.Max(4f, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryTileSize.Value)
                : 8f;
            TargetedEventReport targeted = TryApplyTargetedEvent(record, destructive, discoveryTileSize, sdfChunkSize);
            bool dirtiedActiveChunk = false;
            if (!targeted.Applied && (!destructive || hadCachedSource))
            {
                if (record.HasOldBounds) dirtiedActiveChunk |= MarkDiscoveryChunks(record.OldBounds, discoveryTileSize);
                if (record.HasNewBounds) dirtiedActiveChunk |= MarkDiscoveryChunks(record.NewBounds, discoveryTileSize);
            }
            // Destruction callbacks can arrive after Unity has detached an object from its
            // scene. An uncached callback has no trustworthy old bounds, so it must not turn
            // into a whole-domain invalidation. The next low-frequency consistency scan is
            // still responsible for discovering anything that was missed.
            if (!targeted.Applied && !record.HasOldBounds && !record.HasNewBounds && (!destructive || hadCachedSource))
            {
                foreach (ChunkKey key in _activeChunks)
                {
                    _dirtyDiscoveryChunks.Add(key);
                    _discoverySchedule.Request(key);
                    dirtiedActiveChunk = true;
                }
            }

            bool queuedImmediateScan = !targeted.Applied && (hadCachedSource || dirtiedActiveChunk);
            if (queuedImmediateScan)
            {
                _forceScanAfterEvent = true;
                _nextScanTime = 0f;
            }
            if (scheduleDoorFollowup &&
                targeted.Applied &&
                string.Equals(record.Reason, "door/gate state", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(record.SourceId) &&
                source != null)
            {
                _deferredEvents[record.SourceId] = new DeferredEvent
                {
                    Source = source,
                    DueTime = Time.realtimeSinceStartup + 1.25f
                };
            }
            TrimEvents();
            eventWatch.Stop();

            if (PhysicalWaterPlugin.Settings != null && PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry event #" + record.Sequence +
                                                ": dirtyReason=" + record.Reason +
                                                ", sourceId=" + Safe(record.SourceId) +
                                                ", sourceType=" + Safe(record.SourceType) +
                                                ", category=" + record.Category +
                                                ", path=" + Safe(record.Path) +
                                                ", oldAabb=" + (record.HasOldBounds ? FormatBounds(record.OldBounds) : "unknown") +
                                                ", newAabb=" + (record.HasNewBounds ? FormatBounds(record.NewBounds) : "unknown") +
                                                ", cache=" + (hadCachedSource ? "hit" : "miss") +
                                                ", discoveryChunksDirtied=" + _dirtyDiscoveryChunks.Count +
                                                ", unresolvedDestruction=" + (destructive && !hadCachedSource ? "True" : "False") +
                                                ", queuedImmediateScan=" + (queuedImmediateScan ? "True" : "False") +
                                                ", targetedEvent=" + (targeted.Applied ? "True" : "False") +
                                                ", targetedOutcome=" + Safe(targeted.Outcome) +
                                                ", targetedQueuedJobs=" + targeted.QueuedJobs +
                                                ", eventClassificationMs=" + targeted.ClassificationMilliseconds.ToString("F3") +
                                                ", eventRevisionHashMs=" + targeted.RevisionHashMilliseconds.ToString("F3") +
                                                ", eventDirtyDetectionMs=" + targeted.DirtyDetectionMilliseconds.ToString("F3") +
                                                ", eventTotalMs=" + eventWatch.Elapsed.TotalMilliseconds.ToString("F3") + ".");
            }
        }

        private void ProcessDeferredEvents()
        {
            if (_deferredEvents.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            _dueDeferredEventIds.Clear();
            foreach (var kv in _deferredEvents)
            {
                if (kv.Value.Source == null || now >= kv.Value.DueTime) _dueDeferredEventIds.Add(kv.Key);
            }

            for (int i = 0; i < _dueDeferredEventIds.Count; i++)
            {
                string sourceId = _dueDeferredEventIds[i];
                DeferredEvent deferred;
                if (!_deferredEvents.TryGetValue(sourceId, out deferred)) continue;
                _deferredEvents.Remove(sourceId);
                if (deferred.Source != null) MarkDirtyFromValheimEvent("door/gate settled", deferred.Source, false, null);
            }
        }

        private TargetedEventReport TryApplyTargetedEvent(EventRecord record, bool destructive, float discoveryTileSize, float sdfChunkSize)
        {
            var report = new TargetedEventReport { Outcome = "discovery-fallback" };
            if (record == null || string.IsNullOrEmpty(record.SourceId) || _activeChunks.Count == 0) return report;

            bool intersectsActive = (record.HasOldBounds && BoundsIntersectsActiveChunk(record.OldBounds, discoveryTileSize)) ||
                                    (record.HasNewBounds && BoundsIntersectsActiveChunk(record.NewBounds, discoveryTileSize));
            if (!intersectsActive)
            {
                report.Outcome = "outside-active-domain";
                return report;
            }

            Stopwatch stageWatch = Stopwatch.StartNew();
            CachedSource cached;
            _cache.TryGetValue(record.SourceId, out cached);
            if (destructive)
            {
                if (cached == null)
                {
                    report.Outcome = "uncached-destruction";
                    return report;
                }

                Vector3 center = GetScanCenter();
                float radius = Mathf.Max(8f, PhysicalWaterPlugin.Settings.ValheimGeometryScanRadius.Value);
                float cellSize = Mathf.Max(0.1f, PhysicalWaterPlugin.Settings.ValheimGeometryCellSize.Value);
                int padding = Mathf.Max(0, PhysicalWaterPlugin.Settings.ValheimGeometryDirtyPaddingCells.Value);
                VoxelRegion region = EstimateDirtyRegion(cached.Source.Bounds, true, center, radius, cellSize, padding);
                report.QueuedJobs = LogChangeAndQueue(
                    ValheimWorldGeometryChangeKind.Removed,
                    cached.Source,
                    cached.Source.Bounds,
                    default(Bounds),
                    true,
                    false,
                    region,
                    "targeted-remove",
                    cached.Chunks != null ? cached.Chunks.Count : 0,
                    sdfChunkSize,
                    cellSize);
                RemoveCachedSource(record.SourceId);
                stageWatch.Stop();
                report.DirtyDetectionMilliseconds = stageWatch.Elapsed.TotalMilliseconds;
                report.Applied = true;
                report.Outcome = "removed";
                return report;
            }

            if (record.Root == null || !record.Root.activeInHierarchy)
            {
                report.Outcome = "source-unavailable";
                return report;
            }

            Source source = CreateSource(record.Root);
            Collider[] colliders = record.Root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;
                AccumulateCollider(source, collider);
            }
            stageWatch.Stop();
            report.ClassificationMilliseconds = stageWatch.Elapsed.TotalMilliseconds;
            if (source.Colliders == 0 || !source.CanFeedSdf)
            {
                report.Outcome = "no-usable-solid-collider";
                return report;
            }

            stageWatch.Restart();
            FinalizeSource(source, cached);
            stageWatch.Stop();
            report.RevisionHashMilliseconds = stageWatch.Elapsed.TotalMilliseconds;

            stageWatch.Restart();
            Vector3 scanCenter = GetScanCenter();
            float scanRadius = Mathf.Max(8f, PhysicalWaterPlugin.Settings.ValheimGeometryScanRadius.Value);
            float voxelCellSize = Mathf.Max(0.1f, PhysicalWaterPlugin.Settings.ValheimGeometryCellSize.Value);
            int dirtyPadding = Mathf.Max(0, PhysicalWaterPlugin.Settings.ValheimGeometryDirtyPaddingCells.Value);
            HashSet<ChunkKey> discoveryMemberships = ComputeWorldChunks(source.Bounds, discoveryTileSize);
            HashSet<ChunkKey> sourceChunks = ComputeSourceChunks(source.Bounds, scanCenter, scanRadius, voxelCellSize, sdfChunkSize);

            if (cached == null)
            {
                _cache[source.Id] = new CachedSource
                {
                    Source = source,
                    Seen = true,
                    Chunks = sourceChunks,
                    DiscoveryChunks = discoveryMemberships
                };
                AddDiscoveryMemberships(source.Id, discoveryMemberships);
                VoxelRegion region = EstimateDirtyRegion(source.Bounds, true, scanCenter, scanRadius, voxelCellSize, dirtyPadding);
                report.QueuedJobs = LogChangeAndQueue(
                    ValheimWorldGeometryChangeKind.Added,
                    source,
                    default(Bounds),
                    source.Bounds,
                    false,
                    true,
                    region,
                    "targeted-miss",
                    sourceChunks.Count,
                    sdfChunkSize,
                    voxelCellSize);
                report.Outcome = "added";
            }
            else if (cached.Source.Revision != source.Revision)
            {
                Bounds dirtyBounds = source.Bounds;
                dirtyBounds.Encapsulate(cached.Source.Bounds);
                ReplaceDiscoveryMemberships(source.Id, cached.DiscoveryChunks, discoveryMemberships);
                VoxelRegion region = EstimateDirtyRegion(dirtyBounds, true, scanCenter, scanRadius, voxelCellSize, dirtyPadding);
                report.QueuedJobs = LogChangeAndQueue(
                    ValheimWorldGeometryChangeKind.MovedOrChanged,
                    source,
                    cached.Source.Bounds,
                    source.Bounds,
                    true,
                    true,
                    region,
                    "targeted-hit",
                    sourceChunks.Count,
                    sdfChunkSize,
                    voxelCellSize);
                cached.Source = source;
                cached.Chunks = sourceChunks;
                cached.DiscoveryChunks = discoveryMemberships;
                cached.Seen = true;
                cached.ExplicitlyDirty = false;
                report.Outcome = "changed";
            }
            else
            {
                ConsumeEventForSource(source);
                cached.Seen = true;
                cached.ExplicitlyDirty = false;
                report.Outcome = "unchanged";
            }

            stageWatch.Stop();
            report.DirtyDetectionMilliseconds = stageWatch.Elapsed.TotalMilliseconds;
            report.Applied = true;
            return report;
        }

        private bool BoundsIntersectsActiveChunk(Bounds bounds, float chunkSize)
        {
            float size = Mathf.Max(8f, chunkSize);
            int minX = Mathf.FloorToInt(bounds.min.x / size);
            int maxX = Mathf.FloorToInt(bounds.max.x / size);
            int minZ = Mathf.FloorToInt(bounds.min.z / size);
            int maxZ = Mathf.FloorToInt(bounds.max.z / size);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                if (_activeChunks.Contains(new ChunkKey(x, z))) return true;
            return false;
        }

        private void ScanAndLog()
        {
            Stopwatch invocationWatch = Stopwatch.StartNew();
            Vector3 center = GetScanCenter();
            float radius = Mathf.Max(8f, PhysicalWaterPlugin.Settings.ValheimGeometryScanRadius.Value);
            float cellSize = Mathf.Max(0.1f, PhysicalWaterPlugin.Settings.ValheimGeometryCellSize.Value);
            int padding = Mathf.Max(0, PhysicalWaterPlugin.Settings.ValheimGeometryDirtyPaddingCells.Value);
            float sdfChunkSize = Mathf.Max(8f, PhysicalWaterPlugin.Settings.ValheimGeometryChunkSize.Value);
            float discoveryTileSize = Mathf.Max(4f, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryTileSize.Value);
            float snap = Mathf.Max(1f, PhysicalWaterPlugin.Settings.ValheimGeometryOriginSnapMeters.Value);
            bool recentered = UpdateStreamingCenter(center, snap);
            Bounds scanBounds = new Bounds(_streamCenter, new Vector3(radius * 2f, radius, radius * 2f));

            // devE3.1: an explicit finite-domain coverage request is causal work,
            // not speculative discovery. It must be schedulable even when the
            // normal snapped D6 scan center would leave an edge tile just outside
            // the 64 m player-centered scan radius.
            Bounds effectiveScanBounds = scanBounds;
            if (_discoverySchedule.CoverageWindowActive)
            {
                effectiveScanBounds.Encapsulate(_causalCoverageBounds.min);
                effectiveScanBounds.Encapsulate(_causalCoverageBounds.max);
            }

            StreamingReport streaming = UpdateStreamingChunks(effectiveScanBounds, discoveryTileSize);

            float now = Time.realtimeSinceStartup;
            float consistencyInterval = Mathf.Max(15f, PhysicalWaterPlugin.Settings.ValheimGeometryFullConsistencyInterval.Value);
            bool fullConsistency = now >= _nextConsistencyScanTime;
            if (fullConsistency)
            {
                _nextConsistencyScanTime = now + consistencyInterval;
                if (PhysicalWaterPlugin.Settings.StageE1Enabled.Value && _discoverySchedule.CoverageWindowActive)
                {
                    _discoverySchedule.BeginConsistencySweep(_activeChunks);
                    PhysicalWaterPlugin.Log.LogInfo(
                        "LiquidCore PCE bounded consistency sweep started: sleepingChunks=" + _activeChunks.Count +
                        ", requiredTiles=" + _discoverySchedule.RequiredCount + ".");
                }
            }

            int maxDiscoveryChunks = Mathf.Max(1, PhysicalWaterPlugin.Settings.ValheimGeometryDiscoveryChunksPerScan.Value);
            if (fullConsistency)
                foreach (ChunkKey key in _activeChunks) _pendingDiscoveryChunks.Add(key);
            for (int i = 0; i < _loadedChunks.Count; i++) _pendingDiscoveryChunks.Add(_loadedChunks[i]);
            for (int i = 0; i < _evictedChunks.Count; i++)
            {
                _pendingDiscoveryChunks.Remove(_evictedChunks[i]);
                _priorityDiscoveryChunks.Remove(_evictedChunks[i]);
                _dirtyDiscoveryChunks.Remove(_evictedChunks[i]);
                _discoverySchedule.Remove(_evictedChunks[i]);
            }

            _discoveryChunks.Clear();
            foreach (ChunkKey key in _priorityDiscoveryChunks)
            {
                if (_discoveryChunks.Count >= maxDiscoveryChunks) break;
                if (_activeChunks.Contains(key)) _discoveryChunks.Add(key);
            }
            foreach (ChunkKey key in _dirtyDiscoveryChunks)
            {
                if (!_activeChunks.Contains(key)) continue;
                _pendingDiscoveryChunks.Add(key);
                if (_discoveryChunks.Count < maxDiscoveryChunks) _discoveryChunks.Add(key);
            }
            _dirtyDiscoveryChunks.Clear();
            bool requiredOnly = PhysicalWaterPlugin.Settings.StageE1Enabled.Value && _discoverySchedule.CoverageWindowActive;
            SelectPendingDiscoveryChunks(maxDiscoveryChunks, requiredOnly);
            if (_discoveryChunks.Count > 0) _discoverySchedule.BeginUpdate();
            foreach (ChunkKey key in _discoveryChunks)
            {
                _pendingDiscoveryChunks.Remove(key);
                _priorityDiscoveryChunks.Remove(key);
                _discoverySchedule.Complete(key);
            }
            for (int i = 0; i < _evictedChunks.Count; i++) _discoveryCache.Remove(_evictedChunks[i]);

            Bounds discoveryBounds;
            bool hasDiscoveryBounds = TryGetDiscoveryBounds(_discoveryChunks, effectiveScanBounds, discoveryTileSize, out discoveryBounds);
            if (!hasDiscoveryBounds)
            {
                invocationWatch.Stop();
                double cachedTotalMs = invocationWatch.Elapsed.TotalMilliseconds;
                bool cachedWarning = cachedTotalMs > PhysicalWaterPlugin.Settings.ValheimGeometryFrameBudgetMilliseconds.Value;
                _scanIndex++;
                if (PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value)
                {
                    PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry scan #" + _scanIndex +
                                                ": mode=cached-reuse, center=" + Format(_streamCenter) +
                                                ", radius=" + radius.ToString("F1") + "m" +
                                                ", discoveryTileSize=" + discoveryTileSize.ToString("F1") + "m" +
                                                ", sdfChunkSize=" + sdfChunkSize.ToString("F1") + "m" +
                                                ", recentered=" + recentered +
                                                ", chunksActive=" + _activeChunks.Count +
                                                ", chunksReused=" + _activeChunks.Count +
                                                ", chunksDiscovered=0" +
                                                ", pendingDiscoveryChunks=" + _pendingDiscoveryChunks.Count +
                                                ", chunksLoaded=" + streaming.Loaded +
                                                ", chunksEvicted=" + streaming.Evicted +
                                                ", sourcesCached=" + _cache.Count +
                                                ", cacheHits=" + _cache.Count +
                                                ", cacheMisses=0, cacheHitRate=1.000" +
                                                ", added=0, changed=0, removed=0, falseGeometryChanges=0" +
                                                ", pendingJobs=" + _workQueue.Count +
                                                ", physicsOverlapMs=0.000, hierarchyRootMs=0.000, classificationMs=0.000" +
                                                ", revisionHashMs=0.000, heightmapMs=0.000, cacheLookupMs=0.000" +
                                                ", dirtyDetectionMs=0.000, queueWorkMs=" + _lastQueueActiveCpuMs.ToString("F3") +
                                                ", scanMs=" + cachedTotalMs.ToString("F3") +
                                                ", totalMs=" + cachedTotalMs.ToString("F3") +
                                                ", fullConsistency=False, frameBudgetWarning=" + cachedWarning + ".");
                }
                return;
            }

            var profile = new ScanProfile();
            foreach (var kv in _cache)
            {
                CachedSource cached = kv.Value;
                cached.Seen = effectiveScanBounds.Intersects(cached.Source.Bounds) &&
                              !cached.ExplicitlyDirty &&
                              !discoveryBounds.Intersects(cached.Source.Bounds);
            }

            PrepareDiscoveryChunks(_discoveryChunks, now);
            ScanColliders(discoveryBounds, ref profile);
            TerrainReport terrainReport = SampleTerrain(discoveryBounds, cellSize);
            profile.HeightmapMilliseconds = terrainReport.ElapsedMilliseconds;

            int accepted = 0, rejected = 0, terrain = 0, solids = 0, thin = 0, dynamic = 0;
            int mesh = 0, primitive = 0, compound = 0, nonUniform = 0, triggers = 0;
            int characters = 0, items = 0, ships = 0, vegetation = 0, effects = 0, waterVolumes = 0;
            int added = 0, changed = 0, removed = 0, unchanged = 0, queuedJobs = 0;
            int changedSourceChunks = 0, cacheLookups = 0, cacheHits = 0, falseGeometryChanges = 0;
            Bounds dirtyBounds = new Bounds();
            bool hasDirty = false;

            foreach (var kv in _scanRoots)
            {
                Source source = kv.Value;
                Stopwatch stageWatch = Stopwatch.StartNew();
                CachedSource cachedBefore;
                _cache.TryGetValue(source.Id, out cachedBefore);
                stageWatch.Stop();
                profile.CacheLookupMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
                cacheLookups++;
                if (cachedBefore != null) cacheHits++;

                stageWatch.Restart();
                FinalizeSource(source, cachedBefore);
                stageWatch.Stop();
                profile.RevisionHashMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
                CountSource(source, ref accepted, ref rejected, ref terrain, ref solids, ref thin, ref dynamic, ref mesh, ref primitive, ref compound, ref nonUniform, ref triggers, ref characters, ref items, ref ships, ref vegetation, ref effects, ref waterVolumes);
                if (!source.CanFeedSdf) continue;

                HashSet<ChunkKey> discoveryMemberships = ComputeWorldChunks(source.Bounds, discoveryTileSize);
                AddDiscoveryMemberships(source.Id, discoveryMemberships);
                stageWatch.Restart();
                CachedSource cached;
                if (!_cache.TryGetValue(source.Id, out cached))
                {
                    HashSet<ChunkKey> sourceChunks = ComputeSourceChunks(source.Bounds, center, radius, cellSize, sdfChunkSize);
                    _cache[source.Id] = new CachedSource { Source = source, Seen = true, Chunks = sourceChunks, DiscoveryChunks = discoveryMemberships };
                    added++;
                    Encapsulate(ref dirtyBounds, ref hasDirty, source.Bounds);
                    VoxelRegion region = EstimateDirtyRegion(source.Bounds, true, center, radius, cellSize, padding);
                    queuedJobs += LogChangeAndQueue(ValheimWorldGeometryChangeKind.Added, source, default(Bounds), source.Bounds, false, true, region, "miss", sourceChunks.Count, sdfChunkSize, cellSize);
                }
                else
                {
                    cached.Seen = true;
                    bool revisionChanged = cached.Source.Revision != source.Revision;
                    if (revisionChanged)
                    {
                        changed++;
                        Bounds moved = source.Bounds;
                        moved.Encapsulate(cached.Source.Bounds);
                        Encapsulate(ref dirtyBounds, ref hasDirty, moved);
                        HashSet<ChunkKey> sourceChunks = ComputeSourceChunks(source.Bounds, center, radius, cellSize, sdfChunkSize);
                        changedSourceChunks += CountChangedChunks(cached.Chunks, sourceChunks);
                        VoxelRegion region = EstimateDirtyRegion(moved, true, center, radius, cellSize, padding);
                        queuedJobs += LogChangeAndQueue(ValheimWorldGeometryChangeKind.MovedOrChanged, source, cached.Source.Bounds, source.Bounds, true, true, region, "hit", sourceChunks.Count, sdfChunkSize, cellSize);
                        cached.Source = source;
                        cached.Chunks = sourceChunks;
                    }
                    else
                    {
                        unchanged++;
                        if (cached.ExplicitlyDirty) falseGeometryChanges++;
                    }
                    cached.DiscoveryChunks = discoveryMemberships;
                    cached.ExplicitlyDirty = false;
                }
                stageWatch.Stop();
                profile.DirtyDetectionMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
            }

            _removed.Clear();
            foreach (var kv in _cache)
            {
                if (kv.Value.Seen) continue;
                _removed.Add(kv.Key);
                removed++;
                Encapsulate(ref dirtyBounds, ref hasDirty, kv.Value.Source.Bounds);
                VoxelRegion region = EstimateDirtyRegion(kv.Value.Source.Bounds, true, center, radius, cellSize, padding);
                queuedJobs += LogChangeAndQueue(ValheimWorldGeometryChangeKind.Removed, kv.Value.Source, kv.Value.Source.Bounds, default(Bounds), true, false, region, "remove", kv.Value.Chunks != null ? kv.Value.Chunks.Count : 0, sdfChunkSize, cellSize);
            }
            for (int i = 0; i < _removed.Count; i++) RemoveCachedSource(_removed[i]);

            invocationWatch.Stop();
            VoxelRegion dirtyRegion = EstimateDirtyRegion(dirtyBounds, hasDirty, center, radius, cellSize, padding);
            double totalMs = invocationWatch.Elapsed.TotalMilliseconds;
            double scanMs = Math.Max(0.0, totalMs - terrainReport.ElapsedMilliseconds);
            bool frameBudgetWarning = totalMs > PhysicalWaterPlugin.Settings.ValheimGeometryFrameBudgetMilliseconds.Value;
            bool incrementalBudgetWarning = (added + changed + removed) > 0 && totalMs > PhysicalWaterPlugin.Settings.ValheimGeometryIncrementalBudgetMilliseconds.Value;
            double cacheHitRate = cacheLookups > 0 ? cacheHits / (double)cacheLookups : 1.0;
            _scanIndex++;
            if (PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry scan #" + _scanIndex +
                                            ": mode=" + (fullConsistency ? "full-consistency" : "incremental-discovery") +
                                            ", center=" + Format(_streamCenter) +
                                            ", radius=" + radius.ToString("F1") + "m" +
                                            ", discoveryTileSize=" + discoveryTileSize.ToString("F1") + "m" +
                                            ", sdfChunkSize=" + sdfChunkSize.ToString("F1") + "m" +
                                            ", recentered=" + recentered +
                                            ", chunksActive=" + _activeChunks.Count +
                                            ", chunksLoaded=" + streaming.Loaded +
                                            ", chunksEvicted=" + streaming.Evicted +
                                            ", chunksDiscovered=" + _discoveryChunks.Count +
                                            ", chunksReused=" + Mathf.Max(0, _activeChunks.Count - _discoveryChunks.Count) +
                                            ", pendingDiscoveryChunks=" + _pendingDiscoveryChunks.Count +
                                            ", overlapColliders=" + _lastColliderOverlapCount +
                                            ", overlapOverflow=" + _lastColliderOverlapOverflow +
                                            ", candidates=" + _scanRoots.Count +
                                            ", accepted=" + accepted +
                                            ", rejected=" + rejected +
                                            ", cacheHits=" + unchanged +
                                            ", cacheMisses=" + added +
                                            ", sourceCacheLookups=" + cacheLookups +
                                            ", sourceCacheHitRate=" + cacheHitRate.ToString("F3") +
                                            ", sourcesCached=" + _cache.Count +
                                            ", terrain=" + terrain +
                                            ", solids=" + solids +
                                            ", thin=" + thin +
                                            ", dynamic=" + dynamic +
                                            ", mesh=" + mesh +
                                            ", primitive=" + primitive +
                                            ", compound=" + compound +
                                            ", nonUniformScale=" + nonUniform +
                                            ", triggers=" + triggers +
                                            ", characters=" + characters +
                                            ", items=" + items +
                                            ", ships=" + ships +
                                            ", vegetation=" + vegetation +
                                            ", effects=" + effects +
                                            ", waterVolumes=" + waterVolumes +
                                            ", added=" + added +
                                            ", changed=" + changed +
                                            ", removed=" + removed +
                                            ", falseGeometryChanges=" + falseGeometryChanges +
                                            ", queuedJobs=" + queuedJobs +
                                            ", pendingJobs=" + _workQueue.Count +
                                            ", worstQueueDepth=" + _queueWorstDepth +
                                            ", staleJobsDiscarded=" + _queueTotalStaleDiscarded +
                                            ", chunksCoalesced=" + _queueTotalCoalesced +
                                            ", changedSourceChunks=" + changedSourceChunks +
                                            ", dirtyCells~=" + dirtyRegion.CellCount +
                                            ", dirtyMin=" + dirtyRegion.Min +
                                            ", dirtyMax=" + dirtyRegion.Max +
                                            ", terrainChunks=" + terrainReport.HeightmapCount +
                                            ", terrainSamples=" + terrainReport.SampleCount +
                                            ", terrainDeferred=" + terrainReport.DeferredSamples +
                                            ", terrainOccupiedColumns~=" + terrainReport.EstimatedOccupiedColumns +
                                            ", terrainColliderCompare=" + terrainReport.ColliderCompareCount +
                                            ", terrainMaxError=" + terrainReport.MaxColliderError.ToString("F3") + "m" +
                                            ", terrainMeanError=" + terrainReport.MeanColliderError.ToString("F3") + "m" +
                                            ", terrainMs=" + terrainReport.ElapsedMilliseconds.ToString("F3") +
                                            ", physicsOverlapMs=" + profile.PhysicsOverlapMilliseconds.ToString("F3") +
                                            ", hierarchyRootMs=" + profile.HierarchyRootMilliseconds.ToString("F3") +
                                            ", classificationMs=" + profile.ClassificationMilliseconds.ToString("F3") +
                                            ", revisionHashMs=" + profile.RevisionHashMilliseconds.ToString("F3") +
                                            ", heightmapMs=" + profile.HeightmapMilliseconds.ToString("F3") +
                                            ", cacheLookupMs=" + profile.CacheLookupMilliseconds.ToString("F3") +
                                            ", dirtyDetectionMs=" + profile.DirtyDetectionMilliseconds.ToString("F3") +
                                            ", queueWorkMs=" + _lastQueueActiveCpuMs.ToString("F3") +
                                            ", scanMs=" + scanMs.ToString("F3") +
                                            ", totalMs=" + totalMs.ToString("F3") +
                                            ", fullConsistency=" + fullConsistency +
                                            ", frameBudgetWarning=" + frameBudgetWarning +
                                            ", incrementalBudgetWarning=" + incrementalBudgetWarning + ".");
            }

            if (PhysicalWaterPlugin.Settings.ValheimGeometryLogRejected.Value) LogRejectedSamples();
        }

        private void ProcessQueuedWork()
        {
            if (_workQueue.Count == 0)
            {
                _lastQueueActiveCpuMs = 0.0;
                _readyGeometryGeneration = _geometryGeneration;
                PublishReadyDirtyWorldBounds();
                return;
            }

            double budget = Math.Max(0.25, PhysicalWaterPlugin.Settings.ValheimGeometryIncrementalBudgetMilliseconds.Value);
            double usedThisFrame = 0.0;
            int completed = 0;
            int stale = 0;
            string lastStage = "none";

            while (_workQueue.Count > 0)
            {
                DirtyChunkJob job = _workQueue.Peek();
                if (!IsCurrent(job))
                {
                    _workQueue.Dequeue();
                    ReleasePendingJob(job);
                    stale++;
                    _queueBatchStaleDiscarded++;
                    _queueTotalStaleDiscarded++;
                    continue;
                }

                double remainingBudget = budget - usedThisFrame;
                if (remainingBudget <= 0.0001) break;
                double used = job.Consume(remainingBudget, out lastStage);
                if (used <= 0.0001) break;
                usedThisFrame += used;
                if (lastStage == "occupancy") _queueBatchOccupancyMs += used;
                else if (lastStage == "sdf") _queueBatchSdfMs += used;
                else if (lastStage == "upload") _queueBatchUploadMs += used;

                if (job.Complete)
                {
                    _workQueue.Dequeue();
                    CompleteJob(job);
                    completed++;
                }
            }

            if (usedThisFrame <= 0.0001 && completed == 0 && stale == 0) return;

            _queueBatchFrames++;
            if (usedThisFrame > _queueBatchWorstFrameMs) _queueBatchWorstFrameMs = usedThisFrame;
            _lastQueueActiveCpuMs = usedThisFrame;
            if (usedThisFrame > budget + 0.0001) _queueBatchOverruns++;
            if (_workQueue.Count > _queueWorstDepth) _queueWorstDepth = _workQueue.Count;

            bool batchComplete = _workQueue.Count == 0 && _queueBatchQueuedJobs > 0;
            float activeLatencyMs = batchComplete ? (Time.realtimeSinceStartup - _queueBatchStartTime) * 1000f : 0f;

            if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry queue frame: batch=" + _queueBatchSequence +
                                                ", activeCpuMs=" + usedThisFrame.ToString("F3") +
                                                ", budgetMs=" + budget.ToString("F3") +
                                                ", pendingChunks=" + _workQueue.Count +
                                                ", completedChunks=" + completed +
                                                ", staleJobsDiscardedFrame=" + stale +
                                                ", staleJobsDiscardedTotal=" + _queueTotalStaleDiscarded +
                                                ", chunksCoalescedTotal=" + _queueTotalCoalesced +
                                                ", worstQueueDepth=" + _queueWorstDepth +
                                                ", lastStage=" + lastStage +
                                                ", queueLatencyMs=" + (batchComplete ? activeLatencyMs.ToString("F1") : "pending") +
                                                ", totalCompletionLatencyMs=" + (batchComplete ? activeLatencyMs.ToString("F1") : "pending") +
                                                ", batchFrames=" + _queueBatchFrames +
                                                ", batchWorstFrameMs=" + _queueBatchWorstFrameMs.ToString("F3") +
                                                ", batchOccupancyMs=" + _queueBatchOccupancyMs.ToString("F3") +
                                                ", batchSdfMs=" + _queueBatchSdfMs.ToString("F3") +
                                                ", batchUploadMs=" + _queueBatchUploadMs.ToString("F3") +
                                                ", batchComplete=" + batchComplete + ".");
            }

            if (batchComplete)
            {
                _readyGeometryGeneration = _geometryGeneration;
                PublishReadyDirtyWorldBounds();
                ResetQueueBatch();
            }
        }

        private void AccumulatePendingDirtyWorldBounds(Bounds bounds)
        {
            if (!_hasPendingDirtyWorldBounds)
            {
                _pendingDirtyWorldBounds = bounds;
                _hasPendingDirtyWorldBounds = true;
            }
            else _pendingDirtyWorldBounds.Encapsulate(bounds);
        }

        private void PublishReadyDirtyWorldBounds()
        {
            if (_readyDirtyBoundsGeneration == _geometryGeneration) return;
            _readyDirtyBoundsGeneration = _geometryGeneration;
            _hasReadyDirtyWorldBounds = _hasPendingDirtyWorldBounds;
            if (_hasPendingDirtyWorldBounds) _readyDirtyWorldBounds = _pendingDirtyWorldBounds;
            _hasPendingDirtyWorldBounds = false;
        }

        private int LogChangeAndQueue(
            ValheimWorldGeometryChangeKind changeKind,
            Source source,
            Bounds oldBounds,
            Bounds newBounds,
            bool hasOldBounds,
            bool hasNewBounds,
            VoxelRegion dirtyRegion,
            string cacheState,
            int sourceChunkCount,
            float chunkSize,
            float cellSize)
        {
            EventRecord record = ConsumeEventForSource(source);
            string reason = record != null ? record.Reason : changeKind.ToString();
            int queued = EnqueueDirtyChunks(dirtyRegion, reason, source, changeKind, sourceChunkCount, chunkSize, cellSize);
            if (dirtyRegion.Valid) _geometryGeneration++;
            if (dirtyRegion.Valid && source != null)
            {
                PceGeometryChanged?.Invoke(new ValheimPceGeometryChange
                {
                    SourceId = source.Id,
                    Category = source.Category,
                    ChangeKind = changeKind,
                    OldWorldBounds = oldBounds,
                    NewWorldBounds = newBounds,
                    DirtyMin = dirtyRegion.Min,
                    DirtyMax = dirtyRegion.Max,
                    Revision = (uint)Mathf.Max(0, source.Revision),
                    Root = source.Root,
                    Kind = source.Kind,
                    HierarchyPath = source.Path,
                    RootType = source.RootType,
                    CanFeedSdf = source.CanFeedSdf
                });
            }

            bool eventDetail = record != null || changeKind != ValheimWorldGeometryChangeKind.Added;
            if (eventDetail && PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry change: dirtyReason=" + reason +
                                                ", change=" + changeKind +
                                                ", sourceId=" + source.Id +
                                                ", sourceType=" + source.RootType +
                                                ", category=" + source.Category +
                                                ", kind=" + source.Kind +
                                                ", oldAabb=" + (hasOldBounds ? FormatBounds(oldBounds) : "none") +
                                                ", newAabb=" + (hasNewBounds ? FormatBounds(newBounds) : "none") +
                                                ", dirtyMin=" + dirtyRegion.Min +
                                                ", dirtyMax=" + dirtyRegion.Max +
                                                ", dirtyCells=" + dirtyRegion.CellCount +
                                                ", sourceChunks=" + sourceChunkCount +
                                                ", queuedChunks=" + queued +
                                                ", cache=" + cacheState +
                                                ", occupancyMs~=" + EstimateOccupancyMilliseconds(dirtyRegion.CellCount, sourceChunkCount).ToString("F3") +
                                                ", sdfMs~=" + EstimateSdfMilliseconds(dirtyRegion.CellCount).ToString("F3") +
                                                ", uploadMs~=" + EstimateUploadMilliseconds(dirtyRegion.CellCount).ToString("F3") +
                                                ", totalMs~=" + EstimateTotalMilliseconds(dirtyRegion.CellCount, sourceChunkCount).ToString("F3") + ".");
            }

            return queued;
        }

        private int EnqueueDirtyChunks(
            VoxelRegion region,
            string reason,
            Source source,
            ValheimWorldGeometryChangeKind changeKind,
            int sourceMemberships,
            float chunkSize,
            float cellSize)
        {
            if (!region.Valid || region.CellCount <= 0) return 0;
            int cellsPerChunk = Mathf.Max(1, Mathf.CeilToInt(chunkSize / Mathf.Max(0.1f, cellSize)));
            int minChunkX = Mathf.FloorToInt(region.Min.x / (float)cellsPerChunk);
            int maxChunkX = Mathf.FloorToInt(region.Max.x / (float)cellsPerChunk);
            int minChunkZ = Mathf.FloorToInt(region.Min.z / (float)cellsPerChunk);
            int maxChunkZ = Mathf.FloorToInt(region.Max.z / (float)cellsPerChunk);
            int queued = 0;
            int coalesced = 0;
            EnsureQueueBatch();

            for (int z = minChunkZ; z <= maxChunkZ; z++)
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                ChunkKey key = new ChunkKey(x, z);
                ChunkState state;
                if (!_chunkCache.TryGetValue(key, out state))
                {
                    state = new ChunkState();
                    _chunkCache.Add(key, state);
                }

                if (state.Pending)
                {
                    coalesced++;
                    _queueBatchCoalesced++;
                    _queueTotalCoalesced++;
                }

                state.Revision++;
                state.Pending = true;
                state.SourceMemberships = sourceMemberships;

                VoxelRegion chunkRegion = IntersectChunk(region, x, z, cellsPerChunk);
                DirtyChunkJob pendingJob;
                if (_pendingJobByChunk.TryGetValue(key, out pendingJob) && !pendingJob.Started)
                {
                    VoxelRegion merged = UnionRegion(pendingJob.Region, chunkRegion);
                    int memberships = Math.Max(pendingJob.SourceMemberships, sourceMemberships);
                    double mergedOccupancy = EstimateOccupancyMilliseconds(merged.CellCount, memberships);
                    double mergedSdf = EstimateSdfMilliseconds(merged.CellCount);
                    double mergedUpload = EstimateUploadMilliseconds(merged.CellCount);
                    pendingJob.Generation = state.Revision;
                    pendingJob.Region = merged;
                    pendingJob.Reason = reason;
                    pendingJob.SourceId = source.Id;
                    pendingJob.SourceType = source.RootType;
                    pendingJob.ChangeKind = changeKind;
                    pendingJob.SourceMemberships = memberships;
                    pendingJob.QueuedTime = Time.realtimeSinceStartup;
                    pendingJob.OriginalOccupancyMs = mergedOccupancy;
                    pendingJob.OriginalSdfMs = mergedSdf;
                    pendingJob.OriginalUploadMs = mergedUpload;
                    pendingJob.OccupancyRemainingMs = mergedOccupancy;
                    pendingJob.SdfRemainingMs = mergedSdf;
                    pendingJob.UploadRemainingMs = mergedUpload;
                    state.LastDirtyCells = merged.CellCount;
                    _queueBatchDirtyCells += chunkRegion.CellCount;
                    continue;
                }

                state.LastDirtyCells = chunkRegion.CellCount;
                double occupancy = EstimateOccupancyMilliseconds(chunkRegion.CellCount, sourceMemberships);
                double sdf = EstimateSdfMilliseconds(chunkRegion.CellCount);
                double upload = EstimateUploadMilliseconds(chunkRegion.CellCount);
                var job = new DirtyChunkJob
                {
                    BatchId = _queueBatchSequence,
                    Generation = state.Revision,
                    Key = key,
                    Region = chunkRegion,
                    Reason = reason,
                    SourceId = source.Id,
                    SourceType = source.RootType,
                    ChangeKind = changeKind,
                    SourceMemberships = sourceMemberships,
                    QueuedTime = Time.realtimeSinceStartup,
                    OriginalOccupancyMs = occupancy,
                    OriginalSdfMs = sdf,
                    OriginalUploadMs = upload,
                    OccupancyRemainingMs = occupancy,
                    SdfRemainingMs = sdf,
                    UploadRemainingMs = upload
                };
                _workQueue.Enqueue(job);
                _pendingJobByChunk[key] = job;
                queued++;
                _queueBatchQueuedJobs++;
                _queueBatchDirtyCells += chunkRegion.CellCount;
            }

            if (_workQueue.Count > _queueWorstDepth) _queueWorstDepth = _workQueue.Count;
            if (_workQueue.Count > 0) _discoverySchedule.WakeForExternalWork();
            if (coalesced > 0 && !string.Equals(reason, "Added", StringComparison.Ordinal) && PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry queue coalesced: sourceId=" + source.Id +
                                                ", reason=" + reason +
                                                ", coalescedChunks=" + coalesced +
                                                ", pendingChunks=" + _workQueue.Count +
                                                ", staleJobsWillDiscard=True.");
            }

            return queued;
        }

        private bool IsCurrent(DirtyChunkJob job)
        {
            ChunkState state;
            return _chunkCache.TryGetValue(job.Key, out state) && state.Revision == job.Generation;
        }

        private void CompleteJob(DirtyChunkJob job)
        {
            ChunkState state;
            if (_chunkCache.TryGetValue(job.Key, out state) && state.Revision == job.Generation)
            {
                state.Pending = false;
                state.LastDirtyCells = job.Region.CellCount;
            }
            ReleasePendingJob(job);
            _queueBatchCompletedJobs++;

            if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                float latency = (Time.realtimeSinceStartup - job.QueuedTime) * 1000f;
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry chunk complete: batch=" + job.BatchId +
                                                ", chunk=" + job.Key +
                                                ", generation=" + job.Generation +
                                                ", dirtyReason=" + job.Reason +
                                                ", change=" + job.ChangeKind +
                                                ", sourceId=" + job.SourceId +
                                                ", sourceType=" + job.SourceType +
                                                ", dirtyMin=" + job.Region.Min +
                                                ", dirtyMax=" + job.Region.Max +
                                                ", dirtyCells=" + job.Region.CellCount +
                                                ", sourceMemberships=" + job.SourceMemberships +
                                                ", occupancyMs=" + job.OriginalOccupancyMs.ToString("F3") +
                                                ", sdfMs=" + job.OriginalSdfMs.ToString("F3") +
                                                ", uploadMs=" + job.OriginalUploadMs.ToString("F3") +
                                                ", totalLatencyMs=" + latency.ToString("F1") +
                                                ", pendingChunks=" + _workQueue.Count + ".");
            }
        }

        private void ReleasePendingJob(DirtyChunkJob job)
        {
            DirtyChunkJob pending;
            if (_pendingJobByChunk.TryGetValue(job.Key, out pending) && ReferenceEquals(pending, job))
                _pendingJobByChunk.Remove(job.Key);
        }

        private void EnsureQueueBatch()
        {
            if (_queueBatchQueuedJobs > 0) return;
            _queueBatchSequence++;
            _queueBatchStartTime = Time.realtimeSinceStartup;
            _queueBatchCompletedJobs = 0;
            _queueBatchDirtyCells = 0;
            _queueBatchFrames = 0;
            _queueBatchOverruns = 0;
            _queueBatchStaleDiscarded = 0;
            _queueBatchCoalesced = 0;
            _queueBatchOccupancyMs = 0.0;
            _queueBatchSdfMs = 0.0;
            _queueBatchUploadMs = 0.0;
            _queueBatchWorstFrameMs = 0.0;
        }

        private void ResetQueueBatch()
        {
            if (PhysicalWaterPlugin.Settings.Diagnostics.Value)
            {
                float latency = (Time.realtimeSinceStartup - _queueBatchStartTime) * 1000f;
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 geometry queue batch complete: batch=" + _queueBatchSequence +
                                                ", queuedChunks=" + _queueBatchQueuedJobs +
                                                ", completedChunks=" + _queueBatchCompletedJobs +
                                                ", staleJobsDiscarded=" + _queueBatchStaleDiscarded +
                                                ", chunksCoalesced=" + _queueBatchCoalesced +
                                                ", dirtyCells=" + _queueBatchDirtyCells +
                                                ", frames=" + _queueBatchFrames +
                                                ", frameBudgetOverruns=" + _queueBatchOverruns +
                                                ", worstFrameMs=" + _queueBatchWorstFrameMs.ToString("F3") +
                                                ", occupancyMs=" + _queueBatchOccupancyMs.ToString("F3") +
                                                ", sdfMs=" + _queueBatchSdfMs.ToString("F3") +
                                                ", uploadMs=" + _queueBatchUploadMs.ToString("F3") +
                                                ", totalCompletionLatencyMs=" + latency.ToString("F1") +
                                                ", worstQueueDepth=" + _queueWorstDepth + ".");
            }

            _queueBatchQueuedJobs = 0;
            _queueBatchCompletedJobs = 0;
            _queueBatchDirtyCells = 0;
            _queueBatchFrames = 0;
            _queueBatchOverruns = 0;
            _queueBatchStaleDiscarded = 0;
            _queueBatchCoalesced = 0;
            _queueBatchOccupancyMs = 0.0;
            _queueBatchSdfMs = 0.0;
            _queueBatchUploadMs = 0.0;
            _queueBatchWorstFrameMs = 0.0;
        }

        private EventRecord CaptureEvent(string label, Component source)
        {
            var record = new EventRecord
            {
                Sequence = ++_eventSequence,
                Reason = string.IsNullOrEmpty(label) ? "unknown" : label,
                Time = Time.realtimeSinceStartup,
                SourceType = source != null ? source.GetType().Name : "unknown"
            };

            GameObject root = source != null ? FindGeometryRoot(source.gameObject) : null;
            if (root != null)
            {
                record.Root = root;
                record.SourceId = BuildSourceId(root);
                record.Path = HierarchyPath(root.transform);
                string reason;
                record.Category = Classify(root, out reason);
                Bounds bounds;
                if (TryGetRootBounds(root, out bounds) && HasUsableBounds(bounds))
                {
                    record.NewBounds = bounds;
                    record.HasNewBounds = true;
                }

                CachedSource cached;
                if (_cache.TryGetValue(record.SourceId, out cached))
                {
                    record.OldBounds = cached.Source.Bounds;
                    record.HasOldBounds = true;
                }
            }
            else
            {
                record.SourceId = string.Empty;
                record.Path = "unknown";
                record.Category = ValheimWorldGeometryCategory.Unsupported;
            }

            return record;
        }

        private static bool IsDestructiveEvent(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return false;
            string normalized = reason.ToLowerInvariant();
            return normalized.Contains("destroy") || normalized.Contains("hidden") || normalized.Contains("remove");
        }

        private static bool HasUsableBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return IsFinite(size) && size.x > 0.001f && size.y > 0.001f && size.z > 0.001f;
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private EventRecord ConsumeEventForSource(Source source)
        {
            if (source == null) return null;
            EventRecord record;
            if (!_eventBySource.TryGetValue(source.Id, out record)) return null;

            _eventBySource.Remove(source.Id);
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i] == record)
                {
                    _events.RemoveAt(i);
                    break;
                }
            }

            return record;
        }

        private void TrimEvents()
        {
            while (_events.Count > 64)
            {
                EventRecord first = _events[0];
                _events.RemoveAt(0);
                if (!string.IsNullOrEmpty(first.SourceId) && _eventBySource.ContainsKey(first.SourceId) && _eventBySource[first.SourceId] == first)
                {
                    _eventBySource.Remove(first.SourceId);
                }
            }
        }

        private static HashSet<ChunkKey> ComputeWorldChunks(Bounds bounds, float chunkSize)
        {
            var result = new HashSet<ChunkKey>();
            AddWorldChunks(bounds, chunkSize, result);
            return result;
        }

        private static void AddWorldChunks(Bounds bounds, float chunkSize, HashSet<ChunkKey> destination)
        {
            float size = Mathf.Max(8f, chunkSize);
            int minX = Mathf.FloorToInt(bounds.min.x / size);
            int maxX = Mathf.FloorToInt(bounds.max.x / size);
            int minZ = Mathf.FloorToInt(bounds.min.z / size);
            int maxZ = Mathf.FloorToInt(bounds.max.z / size);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++) destination.Add(new ChunkKey(x, z));
        }

        private bool MarkDiscoveryChunks(Bounds bounds, float chunkSize)
        {
            bool marked = false;
            _chunkScratch.Clear();
            AddWorldChunks(bounds, chunkSize, _chunkScratch);
            foreach (ChunkKey key in _chunkScratch)
            {
                if (!_activeChunks.Contains(key)) continue;
                _dirtyDiscoveryChunks.Add(key);
                _discoverySchedule.Request(key);
                marked = true;
            }
            return marked;
        }

        private static bool TryGetDiscoveryBounds(HashSet<ChunkKey> chunks, Bounds scanBounds, float chunkSize, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool hasBounds = false;
            float size = Mathf.Max(8f, chunkSize);
            foreach (ChunkKey key in chunks)
            {
                Bounds chunkBounds = new Bounds(
                    new Vector3((key.X + 0.5f) * size, scanBounds.center.y, (key.Z + 0.5f) * size),
                    new Vector3(size, scanBounds.size.y, size));
                if (!chunkBounds.Intersects(scanBounds)) continue;
                Vector3 min = Vector3.Max(chunkBounds.min, scanBounds.min);
                Vector3 max = Vector3.Min(chunkBounds.max, scanBounds.max);
                Bounds clipped = new Bounds((min + max) * 0.5f, max - min);
                if (!hasBounds)
                {
                    bounds = clipped;
                    hasBounds = true;
                }
                else bounds.Encapsulate(clipped);
            }
            return hasBounds;
        }

        private void PrepareDiscoveryChunks(HashSet<ChunkKey> chunks, float now)
        {
            foreach (ChunkKey key in chunks)
            {
                DiscoveryChunk chunk;
                if (!_discoveryCache.TryGetValue(key, out chunk))
                {
                    chunk = new DiscoveryChunk();
                    _discoveryCache.Add(key, chunk);
                }
                chunk.SourceIds.Clear();
                chunk.Revision++;
                chunk.LastDiscoveryTime = now;
            }
        }

        private void SelectPendingDiscoveryChunks(int maxChunks, bool requiredOnly)
        {
            while (_discoveryChunks.Count < maxChunks)
            {
                bool found = false;
                ChunkKey best = default(ChunkKey);
                int bestDistance = int.MaxValue;
                foreach (ChunkKey candidate in _pendingDiscoveryChunks)
                {
                    if (!_activeChunks.Contains(candidate) || _discoveryChunks.Contains(candidate)) continue;
                    if (requiredOnly && !_discoverySchedule.IsRequired(candidate)) continue;
                    int distance = 0;
                    if (_discoveryChunks.Count > 0)
                    {
                        distance = int.MaxValue;
                        foreach (ChunkKey selected in _discoveryChunks)
                            distance = Math.Min(distance, Math.Abs(candidate.X - selected.X) + Math.Abs(candidate.Z - selected.Z));
                    }
                    if (!found || distance < bestDistance)
                    {
                        found = true;
                        best = candidate;
                        bestDistance = distance;
                    }
                }
                if (!found) break;
                _discoveryChunks.Add(best);
            }
        }

        private void AddDiscoveryMemberships(string sourceId, HashSet<ChunkKey> memberships)
        {
            foreach (ChunkKey key in memberships)
            {
                if (!_activeChunks.Contains(key)) continue;
                DiscoveryChunk chunk;
                if (!_discoveryCache.TryGetValue(key, out chunk))
                {
                    chunk = new DiscoveryChunk();
                    _discoveryCache.Add(key, chunk);
                }
                chunk.SourceIds.Add(sourceId);
            }
        }

        private void ReplaceDiscoveryMemberships(string sourceId, HashSet<ChunkKey> oldMemberships, HashSet<ChunkKey> newMemberships)
        {
            if (oldMemberships != null)
            {
                foreach (ChunkKey key in oldMemberships)
                {
                    DiscoveryChunk chunk;
                    if (_discoveryCache.TryGetValue(key, out chunk)) chunk.SourceIds.Remove(sourceId);
                }
            }
            AddDiscoveryMemberships(sourceId, newMemberships);
        }

        private void RemoveCachedSource(string sourceId)
        {
            CachedSource cached;
            if (!_cache.TryGetValue(sourceId, out cached)) return;
            if (cached.DiscoveryChunks != null)
            {
                foreach (ChunkKey key in cached.DiscoveryChunks)
                {
                    DiscoveryChunk chunk;
                    if (_discoveryCache.TryGetValue(key, out chunk)) chunk.SourceIds.Remove(sourceId);
                }
            }
            _cache.Remove(sourceId);
        }

        private static HashSet<ChunkKey> ComputeSourceChunks(Bounds bounds, Vector3 center, float radius, float cellSize, float chunkSize)
        {
            VoxelRegion region = EstimateDirtyRegion(bounds, true, center, radius, cellSize, 0);
            int cellsPerChunk = Mathf.Max(1, Mathf.CeilToInt(chunkSize / Mathf.Max(0.1f, cellSize)));
            var result = new HashSet<ChunkKey>();
            if (!region.Valid) return result;
            int minChunkX = Mathf.FloorToInt(region.Min.x / (float)cellsPerChunk);
            int maxChunkX = Mathf.FloorToInt(region.Max.x / (float)cellsPerChunk);
            int minChunkZ = Mathf.FloorToInt(region.Min.z / (float)cellsPerChunk);
            int maxChunkZ = Mathf.FloorToInt(region.Max.z / (float)cellsPerChunk);
            for (int z = minChunkZ; z <= maxChunkZ; z++)
            for (int x = minChunkX; x <= maxChunkX; x++)
            {
                result.Add(new ChunkKey(x, z));
            }
            return result;
        }

        private static int CountChangedChunks(HashSet<ChunkKey> oldChunks, HashSet<ChunkKey> newChunks)
        {
            if (oldChunks == null) return newChunks != null ? newChunks.Count : 0;
            if (newChunks == null) return oldChunks.Count;
            int changed = 0;
            foreach (ChunkKey key in newChunks) if (!oldChunks.Contains(key)) changed++;
            foreach (ChunkKey key in oldChunks) if (!newChunks.Contains(key)) changed++;
            return changed;
        }

        private static VoxelRegion IntersectChunk(VoxelRegion region, int chunkX, int chunkZ, int cellsPerChunk)
        {
            int minX = chunkX * cellsPerChunk;
            int maxX = minX + cellsPerChunk - 1;
            int minZ = chunkZ * cellsPerChunk;
            int maxZ = minZ + cellsPerChunk - 1;
            Vector3Int min = new Vector3Int(Mathf.Max(region.Min.x, minX), region.Min.y, Mathf.Max(region.Min.z, minZ));
            Vector3Int max = new Vector3Int(Mathf.Min(region.Max.x, maxX), region.Max.y, Mathf.Min(region.Max.z, maxZ));
            int cells = Mathf.Max(0, max.x - min.x + 1) * Mathf.Max(0, max.y - min.y + 1) * Mathf.Max(0, max.z - min.z + 1);
            return new VoxelRegion { Min = min, Max = max, CellCount = cells, Valid = cells > 0 };
        }

        private static VoxelRegion UnionRegion(VoxelRegion a, VoxelRegion b)
        {
            if (!a.Valid) return b;
            if (!b.Valid) return a;
            Vector3Int min = Vector3Int.Min(a.Min, b.Min);
            Vector3Int max = Vector3Int.Max(a.Max, b.Max);
            int cells = Mathf.Max(0, max.x - min.x + 1) * Mathf.Max(0, max.y - min.y + 1) * Mathf.Max(0, max.z - min.z + 1);
            return new VoxelRegion { Min = min, Max = max, CellCount = cells, Valid = cells > 0 };
        }

        private static double EstimateOccupancyMilliseconds(int cells, int sourceMemberships)
        {
            return Math.Max(0.02, cells * (0.0000025 + Math.Max(1, sourceMemberships) * 0.00000015));
        }

        private static double EstimateSdfMilliseconds(int cells)
        {
            return Math.Max(0.02, cells * 0.0000045);
        }

        private static double EstimateUploadMilliseconds(int cells)
        {
            return Math.Max(0.01, cells * 0.0000007);
        }

        private static double EstimateTotalMilliseconds(int cells, int sourceMemberships)
        {
            return EstimateOccupancyMilliseconds(cells, sourceMemberships) + EstimateSdfMilliseconds(cells) + EstimateUploadMilliseconds(cells);
        }

        private void ScanColliders(Bounds scanBounds, ref ScanProfile profile)
        {
            _scanRoots.Clear();
            Stopwatch stageWatch = Stopwatch.StartNew();
            _lastColliderOverlapCount = Physics.OverlapBoxNonAlloc(
                scanBounds.center,
                scanBounds.extents,
                _colliderScratch,
                Quaternion.identity,
                ~0,
                QueryTriggerInteraction.Collide);
            stageWatch.Stop();
            profile.PhysicsOverlapMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
            _lastColliderOverlapOverflow = _lastColliderOverlapCount >= _colliderScratch.Length;

            for (int i = 0; i < _lastColliderOverlapCount; i++)
            {
                Collider collider = _colliderScratch[i];
                _colliderScratch[i] = null;
                if (collider == null || !collider.enabled) continue;
                stageWatch.Restart();
                GameObject root = FindGeometryRoot(collider.gameObject);
                stageWatch.Stop();
                profile.HierarchyRootMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
                if (root == null || !root.activeInHierarchy) continue;
                if (!scanBounds.Intersects(collider.bounds)) continue;

                int id = root.GetInstanceID();
                Source source;
                if (!_scanRoots.TryGetValue(id, out source))
                {
                    stageWatch.Restart();
                    source = CreateSource(root);
                    Collider[] sourceColliders = root.GetComponentsInChildren<Collider>(true);
                    for (int colliderIndex = 0; colliderIndex < sourceColliders.Length; colliderIndex++)
                    {
                        Collider sourceCollider = sourceColliders[colliderIndex];
                        if (sourceCollider == null || !sourceCollider.enabled) continue;
                        AccumulateCollider(source, sourceCollider);
                    }
                    stageWatch.Stop();
                    profile.ClassificationMilliseconds += stageWatch.Elapsed.TotalMilliseconds;
                    _scanRoots.Add(id, source);
                }
            }
        }

        private static void AccumulateCollider(Source source, Collider collider)
        {
            if (source.Colliders == 0) source.Bounds = collider.bounds;
            else source.Bounds.Encapsulate(collider.bounds);

            source.Colliders++;
            if (collider.isTrigger) source.TriggerColliders++;
            MeshCollider meshCollider = collider as MeshCollider;
            if (meshCollider != null)
            {
                source.MeshColliders++;
                if (meshCollider.sharedMesh != null)
                {
                    source.MeshVertices += meshCollider.sharedMesh.vertexCount;
                    source.MeshTriangles += GetMeshTriangleCount(meshCollider.sharedMesh);
                }
            }
            else if (collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider)
            {
                source.PrimitiveColliders++;
            }
        }

        private bool UpdateStreamingCenter(Vector3 focus, float snap)
        {
            if (float.IsPositiveInfinity(_streamCenter.x))
            {
                _streamCenter = Snap(focus, snap);
                return true;
            }

            Vector3 delta = focus - _streamCenter;
            delta.y = 0f;
            if (delta.sqrMagnitude < snap * snap)
            {
                return false;
            }

            _streamCenter = Snap(focus, snap);
            return true;
        }

        private StreamingReport UpdateStreamingChunks(Bounds scanBounds, float chunkSize)
        {
            _nextChunks.Clear();
            _loadedChunks.Clear();
            _evictedChunks.Clear();
            int minX = Mathf.FloorToInt(scanBounds.min.x / chunkSize);
            int maxX = Mathf.FloorToInt(scanBounds.max.x / chunkSize);
            int minZ = Mathf.FloorToInt(scanBounds.min.z / chunkSize);
            int maxZ = Mathf.FloorToInt(scanBounds.max.z / chunkSize);
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                _nextChunks.Add(new ChunkKey(x, z));
            }

            int loaded = 0;
            foreach (ChunkKey key in _nextChunks)
            {
                if (!_activeChunks.Contains(key))
                {
                    loaded++;
                    _loadedChunks.Add(key);
                }
            }

            int evicted = 0;
            foreach (ChunkKey key in _activeChunks)
            {
                if (!_nextChunks.Contains(key))
                {
                    evicted++;
                    _evictedChunks.Add(key);
                }
            }

            _activeChunks.Clear();
            foreach (ChunkKey key in _nextChunks) _activeChunks.Add(key);
            return new StreamingReport { Loaded = loaded, Evicted = evicted };
        }

        private TerrainReport SampleTerrain(Bounds scanBounds, float voxelCellSize)
        {
            Stopwatch watch = Stopwatch.StartNew();
            var report = new TerrainReport();
            float spacing = Mathf.Max(0.25f, PhysicalWaterPlugin.Settings.ValheimGeometryTerrainSampleSpacing.Value);
            int maxSamples = Mathf.Max(128, PhysicalWaterPlugin.Settings.ValheimGeometryTerrainMaxSamplesPerScan.Value);
            int heightColumns = Mathf.Max(1, Mathf.CeilToInt(scanBounds.size.y / Mathf.Max(0.1f, voxelCellSize)));

            _heightmapsScratch.Clear();
            try
            {
                Heightmap.FindHeightmap(scanBounds.center, Mathf.Max(scanBounds.extents.x, scanBounds.extents.z) + spacing, _heightmapsScratch);
            }
            catch
            {
                List<Heightmap> all = Heightmap.GetAllHeightmaps();
                if (all != null) _heightmapsScratch.AddRange(all);
            }

            report.HeightmapCount = _heightmapsScratch.Count;
            for (int i = 0; i < _heightmapsScratch.Count; i++)
            {
                Heightmap hmap = _heightmapsScratch[i];
                if (hmap == null || !hmap.gameObject.activeInHierarchy) continue;
                Bounds bounds = GetHeightmapWorldBounds(hmap);
                if (!bounds.Intersects(scanBounds)) continue;

                float minX = Mathf.Max(bounds.min.x, scanBounds.min.x);
                float maxX = Mathf.Min(bounds.max.x, scanBounds.max.x);
                float minZ = Mathf.Max(bounds.min.z, scanBounds.min.z);
                float maxZ = Mathf.Min(bounds.max.z, scanBounds.max.z);
                for (float z = minZ; z <= maxZ; z += spacing)
                for (float x = minX; x <= maxX; x += spacing)
                {
                    if (report.SampleCount >= maxSamples)
                    {
                        report.DeferredSamples++;
                        continue;
                    }

                    float height;
                    Vector3 p = new Vector3(x, scanBounds.center.y, z);
                    if (!TryGetHeightmapWorldHeight(hmap, p, out height))
                    {
                        continue;
                    }

                    report.SampleCount++;
                    int below = Mathf.Clamp(Mathf.CeilToInt((height - scanBounds.min.y) / Mathf.Max(0.1f, voxelCellSize)), 0, heightColumns);
                    report.EstimatedOccupiedColumns += below;
                    CompareColliderHeight(hmap, x, z, height, ref report);
                }
            }

            watch.Stop();
            report.ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds;
            if (report.ColliderCompareCount > 0)
            {
                report.MeanColliderError /= report.ColliderCompareCount;
            }
            return report;
        }

        private static Bounds GetHeightmapWorldBounds(Heightmap hmap)
        {
            MeshCollider collider = GetHeightmapField<MeshCollider>(hmap, HeightmapColliderField);
            MeshRenderer renderer = GetHeightmapField<MeshRenderer>(hmap, HeightmapMeshRendererField);
            MeshFilter meshFilter = GetHeightmapField<MeshFilter>(hmap, HeightmapMeshFilterField);

            if (collider != null) return collider.bounds;
            if (renderer != null) return renderer.bounds;
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                Bounds local = meshFilter.sharedMesh.bounds;
                Vector3 c = hmap.transform.TransformPoint(local.center);
                Vector3 e = local.extents;
                Bounds world = new Bounds(c, Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    world.Encapsulate(hmap.transform.TransformPoint(local.center + new Vector3(e.x * x, e.y * y, e.z * z)));
                }
                return world;
            }

            int width = GetHeightmapIntField(hmap, HeightmapWidthField, 64);
            float scale = GetHeightmapFloatField(hmap, HeightmapScaleField, 1f);
            float size = Mathf.Max(1f, width * scale);
            return new Bounds(hmap.transform.position, new Vector3(size, 256f, size));
        }

        private static void CompareColliderHeight(Heightmap hmap, float x, float z, float sampledHeight, ref TerrainReport report)
        {
            MeshCollider collider = GetHeightmapField<MeshCollider>(hmap, HeightmapColliderField);
            if (collider == null) return;
            Bounds bounds = collider.bounds;
            Ray ray = new Ray(new Vector3(x, bounds.max.y + 4f, z), Vector3.down);
            RaycastHit hit;
            if (!collider.Raycast(ray, out hit, bounds.size.y + 8f)) return;
            float error = Mathf.Abs(hit.point.y - sampledHeight);
            report.ColliderCompareCount++;
            report.MeanColliderError += error;
            if (error > report.MaxColliderError) report.MaxColliderError = error;
        }

        private static bool TryGetHeightmapWorldHeight(Heightmap hmap, Vector3 point, out float height)
        {
            height = 0f;
            if (HeightmapGetWorldHeightMethod != null)
            {
                object[] args = { point, height };
                try
                {
                    if (HeightmapGetWorldHeightMethod.Invoke(hmap, args) is bool ok)
                    {
                        height = (float)args[1];
                        if (ok) return true;
                    }
                }
                catch (Exception ex)
                {
                    PhysicalWaterPlugin.Log?.LogDebug("Heightmap.GetWorldHeight reflection failed: " + ex.GetType().Name);
                }
            }

            return Heightmap.GetHeight(point, out height);
        }

        private static T GetHeightmapField<T>(Heightmap hmap, FieldInfo field) where T : class
        {
            return field == null ? null : field.GetValue(hmap) as T;
        }

        private static int GetHeightmapIntField(Heightmap hmap, FieldInfo field, int fallback)
        {
            if (field == null) return fallback;
            object value = field.GetValue(hmap);
            return value is int intValue ? intValue : fallback;
        }

        private static float GetHeightmapFloatField(Heightmap hmap, FieldInfo field, float fallback)
        {
            if (field == null) return fallback;
            object value = field.GetValue(hmap);
            return value is float floatValue ? floatValue : fallback;
        }

        private Source CreateSource(GameObject root)
        {
            string reason;
            ValheimWorldGeometryCategory category = Classify(root, out reason);
            Transform t = root.transform;
            return new Source
            {
                Id = BuildSourceId(root),
                Path = HierarchyPath(t),
                RootType = PrimaryType(root),
                Category = category,
                Kind = ValheimWorldGeometryKind.Unsupported,
                Position = t.position,
                Rotation = t.rotation,
                Scale = t.lossyScale,
                NonUniformScale = IsNonUniform(t.lossyScale),
                CanFeedSdf = CanFeedSdf(category),
                RejectionReason = reason,
                Root = root
            };
        }

        private static void FinalizeSource(Source source, CachedSource cached)
        {
            if (source.Colliders > 1)
            {
                source.Kind = ValheimWorldGeometryKind.CompoundColliderHierarchy;
            }
            else if (source.MeshColliders > 0)
            {
                source.Kind = HasComponentInParents(source.Root, "Heightmap") ? ValheimWorldGeometryKind.HeightmapCollisionMesh : ValheimWorldGeometryKind.MeshCollider;
            }
            else if (source.PrimitiveColliders > 0)
            {
                if (source.Root.GetComponentInChildren<BoxCollider>(true) != null) source.Kind = ValheimWorldGeometryKind.BoxCollider;
                else if (source.Root.GetComponentInChildren<SphereCollider>(true) != null) source.Kind = ValheimWorldGeometryKind.SphereCollider;
                else if (source.Root.GetComponentInChildren<CapsuleCollider>(true) != null) source.Kind = ValheimWorldGeometryKind.CapsuleCollider;
            }

            if (source.TriggerColliders == source.Colliders &&
                source.Colliders > 0 &&
                source.Category != ValheimWorldGeometryCategory.WaterVolume)
            {
                source.Category = ValheimWorldGeometryCategory.Trigger;
                source.CanFeedSdf = false;
                source.RejectionReason = "Trigger-only collider hierarchy.";
            }

            source.CheapRevision = ComputeCheapRevision(source);
            if (cached != null &&
                !cached.ExplicitlyDirty &&
                cached.Source.CheapRevision == source.CheapRevision &&
                !BoundsChanged(cached.Source.Bounds, source.Bounds))
            {
                source.Revision = cached.Source.Revision;
            }
            else
            {
                source.Revision = ComputeRevision(source);
            }
        }

        private static ValheimWorldGeometryCategory Classify(GameObject root, out string reason)
        {
            reason = string.Empty;
            if (IsVanillaLiquidHierarchy(root))
            {
                reason = "vanilla water/ocean/liquid hierarchy excluded";
                return ValheimWorldGeometryCategory.WaterVolume;
            }
            if (HasComponentInHierarchy(root, "Ship", "ShipControlls", "Vagon", "Sadle"))
            {
                reason = "ship/vehicle excluded";
                return ValheimWorldGeometryCategory.VehicleShip;
            }
            if (HasComponentInHierarchy(root, "Player", "Character", "Humanoid", "Fish", "MonsterAI", "AnimalAI", "BaseAI", "RandomFlyingBird", "Tameable") ||
                NameContains(root, "raven", "hugin", "munin", "seagull"))
            {
                reason = "moving gameplay entity excluded";
                return ValheimWorldGeometryCategory.CharacterCreature;
            }
            if (HasComponentInHierarchy(root, "ItemDrop", "Floating", "Projectile") && !HasComponentInHierarchy(root, "Piece"))
            {
                reason = "item/drop/floater excluded";
                return ValheimWorldGeometryCategory.ItemDrop;
            }
            if (HasComponentInHierarchy(root, "TreeBase", "TreeLog", "Pickable") ||
                NameContains(root, "beech", "firtree", "pinetree", "oaktree", "bush", "shrub", "sapling"))
            {
                reason = "vegetation excluded until destructible vegetation policy";
                return ValheimWorldGeometryCategory.Vegetation;
            }
            if (HasComponentInHierarchy(root, "Piece", "WearNTear") && !HasStableNetworkIdentity(root))
            {
                reason = "unplaced build preview excluded";
                return ValheimWorldGeometryCategory.NonSolidDecorative;
            }
            if (HasComponentInParents(root, "Heightmap", "TerrainComp"))
            {
                return ValheimWorldGeometryCategory.Terrain;
            }
            if (HasComponentInParents(root, "Door"))
            {
                return ValheimWorldGeometryCategory.ThinBlockingBarrier;
            }
            if (HasComponentInParents(root, "MineRock", "MineRock5", "Destructible"))
            {
                return ValheimWorldGeometryCategory.DynamicSolid;
            }
            if (HasComponentInParents(root, "Piece", "WearNTear", "DungeonGenerator", "Room", "Location"))
            {
                return ValheimWorldGeometryCategory.SolidBarrier;
            }
            if (HasComponentInChildren(root, "ParticleSystem") || NameContains(root, "vfx", "sfx", "fx_", "smoke", "mist", "splash"))
            {
                reason = "effect/particle hierarchy";
                return ValheimWorldGeometryCategory.ParticleOrEffect;
            }
            return ValheimWorldGeometryCategory.SolidBarrier;
        }

        private static bool CanFeedSdf(ValheimWorldGeometryCategory category)
        {
            return category == ValheimWorldGeometryCategory.SolidBarrier ||
                   category == ValheimWorldGeometryCategory.ThinBlockingBarrier ||
                   category == ValheimWorldGeometryCategory.Terrain ||
                   category == ValheimWorldGeometryCategory.DynamicSolid;
        }

        private static GameObject FindGeometryRoot(GameObject leaf)
        {
            Transform t = leaf != null ? leaf.transform : null;
            Transform best = t;
            while (t != null)
            {
                if (HasAnyComponent(t.gameObject, "Heightmap", "TerrainComp", "Piece", "WearNTear", "Destructible", "Door", "MineRock", "MineRock5", "DungeonGenerator", "Room", "Location", "Ship", "ShipControlls", "Vagon", "Character", "Humanoid", "Player", "Fish", "MonsterAI", "AnimalAI", "BaseAI", "RandomFlyingBird", "ItemDrop", "Floating", "WaterVolume", "LiquidSurface", "LiquidVolume", "TreeBase", "TreeLog", "Pickable"))
                {
                    best = t;
                }
                t = t.parent;
            }
            return best != null ? best.gameObject : leaf;
        }

        private static string BuildSourceId(GameObject root)
        {
            ZNetView view = root.GetComponentInParent<ZNetView>();
            if (view != null)
            {
                string prefab = SafePrefabName(view);
                string zdo = SafeZdoId(view);
                return "znet:" + prefab + ":" + zdo;
            }
            return "go:" + HierarchyPath(root.transform) + ":" + root.GetInstanceID();
        }

        private static int ComputeRevision(Source source)
        {
            return ComputeGeometryRevision(source, true);
        }

        private static int ComputeCheapRevision(Source source)
        {
            return ComputeGeometryRevision(source, false);
        }

        private static int ComputeGeometryRevision(Source source, bool includeMutableGeometry)
        {
            unchecked
            {
                int hash = 23;
                hash = HashTransform(hash, source.Root.transform);
                Collider[] colliders = source.Root.GetComponentsInChildren<Collider>(true);
                hash = hash * 31 + colliders.Length;
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null) continue;
                    hash = hash * 31 + collider.GetType().Name.GetHashCode();
                    hash = hash * 31 + (collider.enabled ? 1 : 0);
                    hash = hash * 31 + (collider.isTrigger ? 1 : 0);
                    hash = HashTransform(hash, collider.transform);
                    BoxCollider box = collider as BoxCollider;
                    SphereCollider sphere = collider as SphereCollider;
                    CapsuleCollider capsule = collider as CapsuleCollider;
                    MeshCollider meshCollider = collider as MeshCollider;
                    if (box != null)
                    {
                        hash = HashVector(hash, box.center);
                        hash = HashVector(hash, box.size);
                    }
                    else if (sphere != null)
                    {
                        hash = HashVector(hash, sphere.center);
                        hash = hash * 31 + Quantize(sphere.radius);
                    }
                    else if (capsule != null)
                    {
                        hash = HashVector(hash, capsule.center);
                        hash = hash * 31 + Quantize(capsule.radius);
                        hash = hash * 31 + Quantize(capsule.height);
                        hash = hash * 31 + capsule.direction;
                    }
                    else if (meshCollider != null)
                    {
                        Mesh mesh = meshCollider.sharedMesh;
                        hash = hash * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
                        hash = hash * 31 + (mesh != null ? mesh.vertexCount : 0);
                        hash = hash * 31 + (mesh != null ? GetMeshTriangleCount(mesh) : 0);
                        hash = hash * 31 + (meshCollider.convex ? 1 : 0);
                    }
                }

                Component[] components = source.Root.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null || !IsGeometryRevisionComponent(component.GetType().Name)) continue;
                    hash = hash * 31 + component.GetType().Name.GetHashCode();
                    hash = hash * 31 + ReadKnownRevision(component);
                    if (includeMutableGeometry && component is Heightmap)
                    {
                        hash = HashHeightmapSamples(hash, (Heightmap)component);
                    }
                }
                return hash;
            }
        }

        private static int HashHeightmapSamples(int hash, Heightmap heightmap)
        {
            List<float> heights = HeightmapHeightsField != null
                ? HeightmapHeightsField.GetValue(heightmap) as List<float>
                : null;
            if (heights == null) return hash;
            unchecked
            {
                hash = hash * 31 + heights.Count;
                for (int i = 0; i < heights.Count; i++) hash = hash * 31 + Quantize(heights[i]);
                return hash;
            }
        }

        private static bool HasStableNetworkIdentity(GameObject root)
        {
            if (root == null) return false;
            ZNetView view = root.GetComponentInParent<ZNetView>();
            if (view == null) view = root.GetComponentInChildren<ZNetView>(true);
            return view != null && view.GetZDO() != null;
        }

        private static int ReadKnownRevision(Component component)
        {
            Type type = component.GetType();
            foreach (string fieldName in new[] { "m_lastDataRevision", "m_operations", "m_allDestroyed", "m_destroyed", "m_picked" })
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) continue;
                object value = field.GetValue(component);
                if (value != null) return value.GetHashCode();
            }
            return 0;
        }

        private static bool IsGeometryRevisionComponent(string typeName)
        {
            return Matches(typeName, "Heightmap", "TerrainComp", "MineRock", "MineRock5", "Destructible", "WearNTear", "Door");
        }

        private static int HashTransform(int hash, Transform transform)
        {
            hash = HashVector(hash, transform.position);
            hash = hash * 31 + Quantize(transform.rotation.x);
            hash = hash * 31 + Quantize(transform.rotation.y);
            hash = hash * 31 + Quantize(transform.rotation.z);
            hash = hash * 31 + Quantize(transform.rotation.w);
            return HashVector(hash, transform.lossyScale);
        }

        private static int HashVector(int hash, Vector3 value)
        {
            hash = hash * 31 + Quantize(value.x);
            hash = hash * 31 + Quantize(value.y);
            return hash * 31 + Quantize(value.z);
        }

        private static int Quantize(float value)
        {
            return Mathf.RoundToInt(value * 1000f);
        }

        private static int GetMeshTriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;
            long indices = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) indices += (long)mesh.GetIndexCount(i);
            return (int)Math.Min(int.MaxValue, indices / 3L);
        }

        private static string SafePrefabName(ZNetView view)
        {
            try
            {
                MethodInfo method = typeof(ZNetView).GetMethod("GetPrefabName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = method != null ? method.Invoke(view, null) : null;
                string name = value as string;
                return string.IsNullOrEmpty(name) ? view.name : name;
            }
            catch
            {
                return view.name;
            }
        }

        private static string SafeZdoId(ZNetView view)
        {
            try
            {
                ZDO zdo = view.GetZDO();
                return zdo != null ? zdo.ToString() : view.GetInstanceID().ToString();
            }
            catch
            {
                return view.GetInstanceID().ToString();
            }
        }

        private static void CountSource(
            Source source,
            ref int accepted,
            ref int rejected,
            ref int terrain,
            ref int solids,
            ref int thin,
            ref int dynamic,
            ref int mesh,
            ref int primitive,
            ref int compound,
            ref int nonUniform,
            ref int triggers,
            ref int characters,
            ref int items,
            ref int ships,
            ref int vegetation,
            ref int effects,
            ref int waterVolumes)
        {
            if (source.CanFeedSdf) accepted++; else rejected++;
            if (source.MeshColliders > 0) mesh++;
            if (source.PrimitiveColliders > 0) primitive++;
            if (source.Kind == ValheimWorldGeometryKind.CompoundColliderHierarchy) compound++;
            if (source.NonUniformScale) nonUniform++;
            switch (source.Category)
            {
                case ValheimWorldGeometryCategory.Terrain: terrain++; break;
                case ValheimWorldGeometryCategory.SolidBarrier: solids++; break;
                case ValheimWorldGeometryCategory.ThinBlockingBarrier: thin++; break;
                case ValheimWorldGeometryCategory.DynamicSolid: dynamic++; break;
                case ValheimWorldGeometryCategory.Trigger: triggers++; break;
                case ValheimWorldGeometryCategory.CharacterCreature: characters++; break;
                case ValheimWorldGeometryCategory.ItemDrop: items++; break;
                case ValheimWorldGeometryCategory.VehicleShip: ships++; break;
                case ValheimWorldGeometryCategory.Vegetation: vegetation++; break;
                case ValheimWorldGeometryCategory.ParticleOrEffect: effects++; break;
                case ValheimWorldGeometryCategory.WaterVolume: waterVolumes++; break;
            }
        }

        private void LogRejectedSamples()
        {
            int logged = 0;
            foreach (var kv in _scanRoots)
            {
                Source source = kv.Value;
                if (source.CanFeedSdf) continue;
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devD6 rejected geometry: category=" + source.Category +
                                                ", reason=" + source.RejectionReason +
                                                ", type=" + source.RootType +
                                                ", path=" + source.Path + ".");
                if (++logged >= 12) break;
            }
        }

        private static Vector3 GetScanCenter()
        {
            if (Player.m_localPlayer != null) return Player.m_localPlayer.transform.position;
            if (Camera.main != null) return Camera.main.transform.position;
            return Vector3.zero;
        }

        private static VoxelRegion EstimateDirtyRegion(Bounds bounds, bool hasDirty, Vector3 center, float radius, float cellSize, int padding)
        {
            if (!hasDirty)
            {
                return default(VoxelRegion);
            }

            Vector3 origin = new Vector3(center.x - radius, center.y - radius * 0.5f, center.z - radius);
            Vector3 localMin = bounds.min - origin;
            Vector3 localMax = bounds.max - origin;
            int nx = Mathf.CeilToInt((radius * 2f) / cellSize);
            int ny = Mathf.CeilToInt(radius / cellSize);
            int nz = Mathf.CeilToInt((radius * 2f) / cellSize);
            Vector3Int min = new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(localMin.x / cellSize) - padding, 0, nx - 1),
                Mathf.Clamp(Mathf.FloorToInt(localMin.y / cellSize) - padding, 0, ny - 1),
                Mathf.Clamp(Mathf.FloorToInt(localMin.z / cellSize) - padding, 0, nz - 1));
            Vector3Int max = new Vector3Int(
                Mathf.Clamp(Mathf.CeilToInt(localMax.x / cellSize) + padding, 0, nx - 1),
                Mathf.Clamp(Mathf.CeilToInt(localMax.y / cellSize) + padding, 0, ny - 1),
                Mathf.Clamp(Mathf.CeilToInt(localMax.z / cellSize) + padding, 0, nz - 1));
            int cells = Mathf.Max(0, max.x - min.x + 1) * Mathf.Max(0, max.y - min.y + 1) * Mathf.Max(0, max.z - min.z + 1);
            return new VoxelRegion { Min = min, Max = max, CellCount = cells, Valid = cells > 0 };
        }

        private static bool TryGetRootBounds(GameObject root, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (root == null) return false;
            bool hasBounds = false;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled) continue;
                if (!hasBounds)
                {
                    bounds = c.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(c.bounds);
                }
            }

            if (hasBounds) return true;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            return hasBounds;
        }

        private static void Encapsulate(ref Bounds dst, ref bool hasDst, Bounds value)
        {
            if (!hasDst)
            {
                dst = value;
                hasDst = true;
            }
            else
            {
                dst.Encapsulate(value);
            }
        }

        private static bool BoundsChanged(Bounds a, Bounds b)
        {
            return (a.center - b.center).sqrMagnitude > 0.0001f || (a.size - b.size).sqrMagnitude > 0.0001f;
        }

        private static bool IsNonUniform(Vector3 scale)
        {
            float max = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float min = Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            return max - min > 0.001f;
        }

        private static bool HasComponentInParents(GameObject root, params string[] names)
        {
            Transform t = root != null ? root.transform : null;
            while (t != null)
            {
                if (HasAnyComponent(t.gameObject, names)) return true;
                t = t.parent;
            }
            return false;
        }

        private static bool HasComponentInHierarchy(GameObject root, params string[] names)
        {
            return HasComponentInParents(root, names) || HasComponentInChildren(root, names);
        }

        private static bool IsVanillaLiquidHierarchy(GameObject root)
        {
            if (HasComponentInHierarchy(root, "WaterVolume", "WaterSurface", "LiquidSurface", "LiquidVolume", "LiquidTrigger"))
                return true;

            Transform current = root != null ? root.transform : null;
            while (current != null)
            {
                string name = StripCloneSuffix(current.name);
                if (string.Equals(name, "Water", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "WaterSurface", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Ocean", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "OceanSurface", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "Liquid", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "LiquidSurface", StringComparison.OrdinalIgnoreCase))
                    return true;
                current = current.parent;
            }
            return false;
        }

        private static string StripCloneSuffix(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            const string suffix = "(Clone)";
            return value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - suffix.Length)
                : value;
        }

        private static bool HasComponentInChildren(GameObject root, params string[] names)
        {
            if (root == null) return false;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
                if (components[i] != null && Matches(components[i].GetType().Name, names)) return true;
            return false;
        }

        private static bool HasAnyComponent(GameObject root, params string[] names)
        {
            if (root == null) return false;
            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
                if (components[i] != null && Matches(components[i].GetType().Name, names)) return true;
            return false;
        }

        private static bool Matches(string value, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
                if (string.Equals(value, names[i], StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool NameContains(GameObject root, params string[] tokens)
        {
            string path = HierarchyPath(root.transform).ToLowerInvariant();
            for (int i = 0; i < tokens.Length; i++)
                if (path.Contains(tokens[i])) return true;
            return false;
        }

        private static string PrimaryType(GameObject root)
        {
            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c != null && !(c is Transform)) return c.GetType().Name;
            }
            return "GameObject";
        }

        private static string HierarchyPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            Transform parent = transform.parent;
            int guard = 0;
            while (parent != null && guard++ < 64)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static string Format(Vector3 v)
        {
            return "(" + v.x.ToString("F1") + "," + v.y.ToString("F1") + "," + v.z.ToString("F1") + ")";
        }

        private static string FormatBounds(Bounds b)
        {
            return "min=" + Format(b.min) + ",max=" + Format(b.max);
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "unknown" : value;
        }

        private static Vector3 Snap(Vector3 v, float snap)
        {
            return new Vector3(
                Mathf.Round(v.x / snap) * snap,
                v.y,
                Mathf.Round(v.z / snap) * snap);
        }

        private struct StreamingReport
        {
            internal int Loaded;
            internal int Evicted;
        }

        private struct VoxelRegion
        {
            internal Vector3Int Min;
            internal Vector3Int Max;
            internal int CellCount;
            internal bool Valid;
        }

        private struct TerrainReport
        {
            internal int HeightmapCount;
            internal int SampleCount;
            internal int DeferredSamples;
            internal int EstimatedOccupiedColumns;
            internal int ColliderCompareCount;
            internal float MaxColliderError;
            internal float MeanColliderError;
            internal double ElapsedMilliseconds;
        }

        private struct ScanProfile
        {
            internal double PhysicsOverlapMilliseconds;
            internal double HierarchyRootMilliseconds;
            internal double ClassificationMilliseconds;
            internal double RevisionHashMilliseconds;
            internal double HeightmapMilliseconds;
            internal double CacheLookupMilliseconds;
            internal double DirtyDetectionMilliseconds;
        }

        private struct ChunkKey : IEquatable<ChunkKey>
        {
            internal readonly int X;
            internal readonly int Z;

            internal ChunkKey(int x, int z)
            {
                X = x;
                Z = z;
            }

            public bool Equals(ChunkKey other)
            {
                return X == other.X && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is ChunkKey && Equals((ChunkKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (X * 397) ^ Z;
                }
            }

            public override string ToString()
            {
                return "(" + X + "," + Z + ")";
            }
        }
    }
}
