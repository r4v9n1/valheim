using System;
using System.Collections.Generic;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class LiquidCorePceRuntime : MonoBehaviour
    {
        private readonly ProbeColonyWorld _world = new ProbeColonyWorld();
        private ProbeColonyGeometryBridge _geometry;
        private ProbeColonyEventBridge _events;
        private PhysicalWaterValheimWorldGeometryAdapter _adapter;
        private ProbeColonyChangeQueue _queue;
        private readonly ProbeColonyCausalGeometrySignalQueue _causalSignals = new ProbeColonyCausalGeometrySignalQueue();
        private readonly Dictionary<string, ValheimPceGeometryChange> _activeSources = new Dictionary<string, ValheimPceGeometryChange>();
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
        private int _publishedEvents;
        internal static LiquidCorePceRuntime Instance { get; private set; }

        internal ProbeColonyWorld World => _world;

        private void Awake()
        {
            _geometry = new ProbeColonyGeometryBridge(_world, new Vector3Int(32, 32, 32));
            _queue = new ProbeColonyChangeQueue(_world, new Vector3Int(32, 32, 32));
            _events = new ProbeColonyEventBridge(_queue);
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
            _publishedEvents++;
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore PCE geometry event #" + _publishedEvents + ": source=" + change.SourceId + ", category=" + change.Category + ", change=" + change.ChangeKind + ", revision=" + change.Revision + ", kind=" + change.Kind + ", root=" + change.RootType + ", region=" + change.DirtyMin + ".." + change.DirtyMax + ".");
            VolumetricWorldGeometryVoxelRegion region = new VolumetricWorldGeometryVoxelRegion { Min = change.DirtyMin, Max = change.DirtyMax, Valid = true };
            VolumetricWorldGeometryCategory pceCategory = ToPceCategory(change.Category);
            _causalSignals.Publish(new ProbeColonyCausalGeometrySignal
            {
                SourceId = change.SourceId,
                ChangeKind = change.ChangeKind == ValheimWorldGeometryChangeKind.Added
                    ? VolumetricWorldGeometryChangeKind.Added
                    : change.ChangeKind == ValheimWorldGeometryChangeKind.Removed
                        ? VolumetricWorldGeometryChangeKind.Removed
                        : VolumetricWorldGeometryChangeKind.MovedOrChanged,
                Category = pceCategory,
                SourceRevision = change.Revision,
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
            if (change.ChangeKind == ValheimWorldGeometryChangeKind.Removed)
            {
                _events.PublishRemoved(change.SourceId, pceCategory, region, change.Revision);
                _activeSources.Remove(change.SourceId);
                _geometry.RemoveSource(change.SourceId);
                return;
            }

            _activeSources[change.SourceId] = change;
            if (change.ChangeKind == ValheimWorldGeometryChangeKind.Added)
                _events.PublishAdded(change.SourceId, pceCategory, region, change.Revision);
            else
                _events.PublishChanged(change.SourceId, pceCategory, region, change.Revision);
            // Do not eagerly materialize the approximate solid occupancy here.
            // A terrain discovery region can cover the entire active window
            // (over one million cells); doing that synchronously from the
            // adapter's scan callback stalls the Unity main thread. Exact
            // collider occupancy is applied at the strict causal
            // synchronization gate in ApplyMappedColliderOccupancy, before
            // the finite-domain solver is released.
        }

        internal bool TryGetActiveSource(string sourceId, out ValheimPceGeometryChange change) => _activeSources.TryGetValue(sourceId, out change);

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
                Collider[] colliders = change.Root.GetComponentsInChildren<Collider>(true);
                if (colliders == null || colliders.Length == 0) continue;
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

        private static ProbeOccupancy SampleColliderOccupancy(Collider[] colliders, Vector3 point, float cellSize)
        {
            const float tolerance = 0.0001f;
            Bounds cell = new Bounds(point, Vector3.one * cellSize);
            bool intersects = false;
            for (int i = 0; i < colliders.Length; i++)
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
            if (_adapter != null) _adapter.PceGeometryChanged -= OnGeometryChanged;
            if (Instance == this) Instance = null;
        }
    }
}
