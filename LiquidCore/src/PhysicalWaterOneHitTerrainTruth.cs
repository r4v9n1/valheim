using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    /// <summary>
    /// One-shot, opt-in-by-build forensic recorder. It never changes solver
    /// state or mathematics. A bounded health capture occurs while arming;
    /// full blocking field downloads begin only after the one terrain hit.
    /// </summary>
    internal sealed class PhysicalWaterOneHitTerrainTruth
    {
        private enum CaptureState { WaitingForBody, Armed, EditDetected, GeometryApplied, Complete }

        private sealed class HeightSnapshot
        {
            internal Heightmap Heightmap;
            internal int InstanceId;
            internal string SourceId;
            internal string Path;
            internal int Width;
            internal int Side;
            internal float Scale;
            internal Vector3 Origin;
            internal float[] Heights;
            internal string Hash;
            internal int PceRevision;
        }

        private sealed class LcSnapshot
        {
            internal string Label;
            internal double SimulatedSeconds;
            internal float[] Sdf;
            internal float[] SolidMask;
            internal float[] Capacity;
            internal float[] CutU;
            internal float[] CutV;
            internal float[] CutW;
            internal float[] Liquid;
            internal float[] U;
            internal float[] V;
            internal float[] W;
            internal VolumetricFlipDomain.FluidParticle[] Particles;
            internal VolumetricFiniteDomainDiagnostics Diagnostics;
            internal double CaptureMilliseconds;
        }

        private static readonly FieldInfo HeightmapWidth = AccessTools.Field(typeof(Heightmap), "m_width");
        private static readonly FieldInfo HeightmapScale = AccessTools.Field(typeof(Heightmap), "m_scale");
        private static readonly FieldInfo HeightmapHeights = AccessTools.Field(typeof(Heightmap), "m_heights");
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private readonly Dictionary<int, HeightSnapshot> _heightmaps = new Dictionary<int, HeightSnapshot>();
        private readonly HashSet<int> _newlyAccessibleCells = new HashSet<int>();
        private CaptureState _state;
        private VolumetricFiniteDomainController _domain;
        private HeightSnapshot _editedBefore;
        private HeightSnapshot _editedAfter;
        private LcSnapshot _preApplyLc;
        private LcSnapshot _postApplyLc;
        private Bounds _editBounds;
        private Vector3 _editPosition;
        private string _operation;
        private string _directory;
        private long _hostEventTicks;
        private long _pceReadyTicks;
        private long _lcReceiveTicks;
        private long _lcAppliedTicks;
        private string _dnaSource;
        private string _dnaAsset;
        private string _dnaCategory;
        private string _dnaChange;
        private int _dnaRevisionBefore;
        private int _dnaRevisionAfter;
        private bool _dnaDatabaseHit;
        private int _dnaRuntimeHandle;
        private int _dnaDependencyHandle;
        private int _postGeometrySteps;
        private int _traceParticleId = -1;
        private double _removedSolidVolume;
        private int _changedSamples;
        private int _loweredSamples;
        private int _raisedSamples;
        private double _newAccessibleVolume;
        private int _openedCells;
        private int _closedCells;
        private int _partialCapacityIncreases;
        private double _openedFaceArea;
        private int _expectedOpenedCells;
        private int _expectedClosedCells;
        private int _falseRemainingSolids;
        private int _falseOpenings;
        private int _missedChangedCells;
        private int _extraChangedCells;
        private string _geometryUpdate;

        internal void TryArm(VolumetricFiniteDomainController domain)
        {
            if (_state != CaptureState.WaitingForBody || domain == null || !domain.Initialized ||
                domain.FlipDomain == null || domain.FlipDomain.HasDeferredStep || domain.FlipDomain.ParticleCount != 4096)
                return;

            var watch = Stopwatch.StartNew();
            VolumetricFiniteDomainDiagnostics health = domain.CaptureDiagnosticsSync(false);
            if (Math.Abs(health.CurrentFluidVolume - 216f) > 0.001f ||
                health.Particles.InvalidParticles != 0 || health.Particles.ParticlesInSolid != 0 ||
                health.Particles.ParticlesOutOfBounds != 0)
                return;

            _domain = domain;
            _directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "R4V9N1", "LiquidCore", "one-hit", DateTime.Now.ToString("yyyyMMdd-HHmmss", Invariant));
            Directory.CreateDirectory(_directory);
            CaptureOverlappingHeightmaps(domain.WorldBounds);
            watch.Stop();
            WriteManifest("armed");
            _state = CaptureState.Armed;
            PhysicalWaterPlugin.Log.LogInfo(
                "ONE-HIT-TEST BEFORE build=2a1eb48-descendant, bodyVolume=" +
                health.CurrentFluidVolume.ToString("F6", Invariant) + "m3, particles=" +
                health.Particles.ParticleCount + ", heightmaps=" + _heightmaps.Count +
                ", captureMs=" + watch.Elapsed.TotalMilliseconds.ToString("F3", Invariant) +
                ", frameStallBudget50ms=" + (watch.Elapsed.TotalMilliseconds < 50.0 ? "PASS" : "FAIL") +
                ", directory=" + _directory + ".");
            PhysicalWaterPlugin.Log.LogInfo("PW_ONE_HIT_READY READY FOR ONE MINING HIT");
        }

        internal void OnTerrainOperationPrefix(Heightmap heightmap, Vector3 position, TerrainOp.Settings modifier, Bounds bounds)
        {
            if (_state != CaptureState.Armed || heightmap == null || !_heightmaps.TryGetValue(heightmap.GetInstanceID(), out HeightSnapshot before))
                return;
            if (!_domain.WorldBounds.Intersects(bounds)) return;
            _editedBefore = before;
            _editPosition = position;
            _editBounds = bounds;
            _operation = Describe(modifier);
            _hostEventTicks = Stopwatch.GetTimestamp();
        }

        internal void OnHeightmapRegenerated(Heightmap heightmap)
        {
            if (_state != CaptureState.Armed || _editedBefore == null || heightmap == null ||
                heightmap.GetInstanceID() != _editedBefore.InstanceId) return;
            HeightSnapshot after = CaptureHeightmap(heightmap);
            if (after == null || after.Heights.Length != _editedBefore.Heights.Length) return;

            var changed = new List<string>();
            double removed = 0.0;
            int changedCount = 0, lowered = 0, raised = 0;
            float sampleArea = after.Scale * after.Scale;
            for (int i = 0; i < after.Heights.Length; i++)
            {
                float oldHeight = _editedBefore.Heights[i];
                float newHeight = after.Heights[i];
                float delta = newHeight - oldHeight;
                if (Math.Abs(delta) <= 1e-6f) continue;
                changedCount++;
                if (delta < 0f) { lowered++; removed += -delta * sampleArea; }
                else raised++;
                int x = i % after.Side;
                int z = i / after.Side;
                Vector3 world = HeightSampleWorld(after, x, z, newHeight);
                changed.Add(x + "," + z + "," + F(oldHeight) + "," + F(newHeight) + "," + F(delta) + "," +
                            F(world.x) + "," + F(world.y) + "," + F(world.z));
            }
            if (changedCount == 0)
            {
                _editedBefore = null;
                PhysicalWaterPlugin.Log.LogWarning("PW_ONE_HIT_NO_TERRAIN_DELTA operation produced no changed Heightmap samples; recorder remains armed.");
                return;
            }

            _editedAfter = after;
            _changedSamples = changedCount;
            _loweredSamples = lowered;
            _raisedSamples = raised;
            _removedSolidVolume = removed;
            File.WriteAllLines(Path.Combine(_directory, "host-terrain-delta.csv"),
                HeaderPlus("x,z,oldHeight,newHeight,delta,worldX,worldY,worldZ", changed));
            _state = CaptureState.EditDetected;
            WriteManifest("edit-detected");
            PhysicalWaterPlugin.Log.LogInfo(
                "ONE-HIT-TEST EDIT DETECTED source=" + after.SourceId + ", operation=" + _operation +
                ", changedSamples=" + changedCount + ", lowered=" + lowered + ", raised=" + raised +
                ", removedSolidVolumeQuadrature=" + removed.ToString("F6", Invariant) + "m3" +
                ", bounds=" + _editBounds + ", terrainHash=" + _editedBefore.Hash + "->" + after.Hash + ".");
        }

        internal bool BeforeLcGeometryApply(
            IReadOnlyList<ProbeColonyCausalGeometrySignal> signals,
            ProbeColonyCausalGeometryBatch batch)
        {
            if (_state != CaptureState.EditDetected || signals == null) return false;
            ProbeColonyCausalGeometrySignal selected = default(ProbeColonyCausalGeometrySignal);
            bool found = false;
            for (int i = 0; i < signals.Count; i++)
            {
                ProbeColonyCausalGeometrySignal signal = signals[i];
                if (signal.Category != VolumetricWorldGeometryCategory.Terrain || !signal.DirtyWorldBounds.Intersects(_editBounds)) continue;
                selected = signal;
                found = true;
                break;
            }
            if (!found) return false;
            _preApplyLc = CaptureLc("PRE_LC_APPLY");
            SaveReplaySnapshot(_preApplyLc, "before-lc-geometry.pwfs");
            _dnaSource = selected.SourceId;
            _dnaAsset = selected.AssetClassId;
            _dnaCategory = selected.Category.ToString();
            _dnaChange = selected.ChangeKind.ToString();
            _dnaRevisionBefore = _editedBefore != null ? _editedBefore.PceRevision : Math.Max(0, unchecked((int)selected.SourceRevision) - 1);
            _dnaRevisionAfter = unchecked((int)selected.SourceRevision);
            _dnaDatabaseHit = selected.DatabaseHit;
            _dnaRuntimeHandle = selected.SourceRuntimeHandle;
            _dnaDependencyHandle = selected.DependencyHandle;
            _pceReadyTicks = selected.ReadyTimestamp;
            _lcReceiveTicks = Stopwatch.GetTimestamp();
            return true;
        }

        internal void AfterLcGeometryApply(VolumetricFiniteSolidUpdateDiagnostics update)
        {
            if (_state != CaptureState.EditDetected || _preApplyLc == null) return;
            _lcAppliedTicks = Stopwatch.GetTimestamp();
            _postApplyLc = CaptureLc("POST_LC_APPLY");
            SaveReplaySnapshot(_postApplyLc, "after-lc-geometry.pwfs");
            _geometryUpdate = update.ToString();
            CalculateGeometryDelta();
            _traceParticleId = SelectTraceParticle(_postApplyLc.Particles);
            _domain.FlipDomain.ClearStepTraceHistory();
            if (_traceParticleId >= 0) _domain.FlipDomain.EnableStepTrace(_traceParticleId);
            WriteFluidSummary(_postApplyLc, "post-geometry");
            WriteManifest("geometry-applied");
            _state = CaptureState.GeometryApplied;
            PhysicalWaterPlugin.Log.LogInfo(
                "PW_ONE_HIT_LC_GEOMETRY source=" + _dnaSource + ", revision=" + _dnaRevisionBefore + "->" + _dnaRevisionAfter +
                ", openedCells=" + _openedCells + ", partialIncreases=" + _partialCapacityIncreases +
                ", closedCells=" + _closedCells + ", newAccessibleVolume=" + _newAccessibleVolume.ToString("F6", Invariant) + "m3" +
                ", openedFaceArea=" + _openedFaceArea.ToString("F6", Invariant) + "m2" +
                ", expectedOpened=" + _expectedOpenedCells + ", expectedClosed=" + _expectedClosedCells +
                ", falseRemainingSolids=" + _falseRemainingSolids + ", falseOpenings=" + _falseOpenings +
                ", missedChanged=" + _missedChangedCells + ", extraChanged=" + _extraChangedCells +
                ", traceParticle=" + _traceParticleId + ".");
        }

        internal void OnStepCompleted()
        {
            if (_state != CaptureState.GeometryApplied) return;
            _postGeometrySteps++;
            if (_postGeometrySteps == 1)
            {
                WriteStageTrace();
                WriteFluidSummary(CaptureLc("STEP_1"), "step-1");
            }
            else if (_postGeometrySteps == 30) WriteFluidSummary(CaptureLc("STEP_30"), "step-30");
            else if (_postGeometrySteps == 120)
            {
                WriteFluidSummary(CaptureLc("STEP_120"), "step-120");
                WriteCodyObservation();
                WriteManifest("complete");
                _state = CaptureState.Complete;
                PhysicalWaterPlugin.Log.LogInfo("PW_ONE_HIT_COMPLETE steps=120, directory=" + _directory + ". User input is complete; offline analysis may proceed.");
            }
        }

        private void CaptureOverlappingHeightmaps(Bounds body)
        {
            _heightmaps.Clear();
            List<Heightmap> all = Heightmap.GetAllHeightmaps();
            if (all == null) return;
            Bounds horizontalBody = body;
            horizontalBody.Expand(new Vector3(4f, 256f, 4f));
            for (int i = 0; i < all.Count; i++)
            {
                Heightmap heightmap = all[i];
                HeightSnapshot snapshot = CaptureHeightmap(heightmap);
                if (snapshot == null) continue;
                float size = Math.Max(1f, snapshot.Width * snapshot.Scale);
                Bounds bounds = new Bounds(snapshot.Origin, new Vector3(size, 512f, size));
                if (!horizontalBody.Intersects(bounds)) continue;
                if (LiquidCorePceRuntime.Instance != null &&
                    LiquidCorePceRuntime.Instance.TryGetSourceRuntimeRecord(snapshot.SourceId, out SourceRuntimeRecord record))
                    snapshot.PceRevision = unchecked((int)record.AuthoritativeRevision);
                _heightmaps[snapshot.InstanceId] = snapshot;
            }
        }

        private static HeightSnapshot CaptureHeightmap(Heightmap heightmap)
        {
            if (heightmap == null || HeightmapHeights == null) return null;
            List<float> source = HeightmapHeights.GetValue(heightmap) as List<float>;
            if (source == null || source.Count == 0) return null;
            int width = HeightmapWidth != null && HeightmapWidth.GetValue(heightmap) is int value ? value : 64;
            int side = Mathf.RoundToInt(Mathf.Sqrt(source.Count));
            if (side * side != source.Count) side = width + 1;
            float scale = HeightmapScale != null && HeightmapScale.GetValue(heightmap) is float scaleValue ? scaleValue : 1f;
            float[] heights = source.ToArray();
            return new HeightSnapshot
            {
                Heightmap = heightmap,
                InstanceId = heightmap.GetInstanceID(),
                SourceId = "go:" + HierarchyPath(heightmap.transform) + ":" + heightmap.GetInstanceID(),
                Path = HierarchyPath(heightmap.transform),
                Width = width,
                Side = side,
                Scale = scale,
                Origin = heightmap.transform.position,
                Heights = heights,
                Hash = HashFloats(heights)
            };
        }

        private LcSnapshot CaptureLc(string label)
        {
            var watch = Stopwatch.StartNew();
            VolumetricWaterDomain mac = _domain.MacDomain;
            var snapshot = new LcSnapshot
            {
                Label = label,
                SimulatedSeconds = mac.Diagnostics.SimulatedSeconds,
                Sdf = _domain.Geometry.CaptureAuthoritativeSdfForForensics(),
                SolidMask = mac.CaptureSolidMaskSync(),
                Capacity = mac.CaptureCutCellCapacitySync(),
                CutU = mac.CaptureCutCellUSync(),
                CutV = mac.CaptureCutCellVSync(),
                CutW = mac.CaptureCutCellWSync(),
                Liquid = mac.CaptureLiquidFractionsSync(),
                U = mac.CaptureUSync(),
                V = mac.CaptureVSync(),
                W = mac.CaptureWSync(),
                Particles = _domain.FlipDomain.CaptureParticleStateSync(),
                Diagnostics = _domain.CaptureDiagnosticsSync(true)
            };
            watch.Stop();
            snapshot.CaptureMilliseconds = watch.Elapsed.TotalMilliseconds;
            return snapshot;
        }

        private void SaveReplaySnapshot(LcSnapshot snapshot, string fileName)
        {
            VolumetricWaterSettings settings = _domain.MacDomain.Settings;
            new VolumetricFluidStateSnapshot
            {
                ResolutionX = settings.ResolutionX,
                ResolutionY = settings.ResolutionY,
                ResolutionZ = settings.ResolutionZ,
                CellSize = settings.CellSize,
                WorldOrigin = _domain.WorldOrigin,
                Particles = snapshot.Particles,
                SolidSdf = snapshot.Sdf,
                SolidMask = snapshot.SolidMask
            }.Save(Path.Combine(_directory, fileName));
        }

        private void CalculateGeometryDelta()
        {
            float[] before = _preApplyLc.Capacity;
            float[] after = _postApplyLc.Capacity;
            VolumetricWaterSettings settings = _domain.MacDomain.Settings;
            float dx = settings.CellSize;
            float cellVolume = dx * dx * dx;
            float faceArea = dx * dx;
            var rows = new List<string>();
            for (int i = 0; i < Math.Min(before.Length, after.Length); i++)
            {
                float delta = after[i] - before[i];
                if (Math.Abs(delta) <= 1e-6f) continue;
                if (before[i] <= 1e-6f && after[i] > 1e-6f) { _openedCells++; _newlyAccessibleCells.Add(i); }
                if (before[i] > 1e-6f && after[i] <= 1e-6f) _closedCells++;
                if (delta > 1e-6f) { _partialCapacityIncreases++; _newAccessibleVolume += delta * cellVolume; _newlyAccessibleCells.Add(i); }
                Vector3Int c = Cell(i, settings.ResolutionX, settings.ResolutionY);
                Vector3 world = _domain.WorldOrigin + new Vector3((c.x + 0.5f) * dx, (c.y + 0.5f) * dx, (c.z + 0.5f) * dx);
                rows.Add(i + "," + c.x + "," + c.y + "," + c.z + "," + F(world.x) + "," + F(world.y) + "," + F(world.z) + "," + F(before[i]) + "," + F(after[i]) + "," + F(delta) + "," + F(_preApplyLc.Sdf[i]) + "," + F(_postApplyLc.Sdf[i]));
            }
            _openedFaceArea = PositiveDelta(_preApplyLc.CutU, _postApplyLc.CutU) * faceArea +
                              PositiveDelta(_preApplyLc.CutV, _postApplyLc.CutV) * faceArea +
                              PositiveDelta(_preApplyLc.CutW, _postApplyLc.CutW) * faceArea;
            CompareHostTruth(settings);
            File.WriteAllLines(Path.Combine(_directory, "lc-geometry-delta.csv"),
                HeaderPlus("index,x,y,z,worldX,worldY,worldZ,capacityBefore,capacityAfter,capacityDelta,sdfBefore,sdfAfter", rows));
        }

        private void CompareHostTruth(VolumetricWaterSettings settings)
        {
            if (_editedBefore == null || _editedAfter == null) return;
            float dx = settings.CellSize;
            int nx = settings.ResolutionX, ny = settings.ResolutionY, nz = settings.ResolutionZ;
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                Vector3 world = _domain.WorldOrigin + new Vector3((x + 0.5f) * dx, (y + 0.5f) * dx, (z + 0.5f) * dx);
                if (world.x < _editBounds.min.x - dx || world.x > _editBounds.max.x + dx ||
                    world.z < _editBounds.min.z - dx || world.z > _editBounds.max.z + dx) continue;
                float oldTerrain = SampleHeight(_editedBefore, world.x, world.z);
                float newTerrain = SampleHeight(_editedAfter, world.x, world.z);
                float oldExpected = Mathf.Clamp01((world.y + 0.5f * dx - oldTerrain) / dx);
                float newExpected = Mathf.Clamp01((world.y + 0.5f * dx - newTerrain) / dx);
                float expectedDelta = newExpected - oldExpected;
                int index = x + nx * (y + ny * z);
                float actualDelta = _postApplyLc.Capacity[index] - _preApplyLc.Capacity[index];
                if (expectedDelta > 1e-4f) _expectedOpenedCells++;
                if (expectedDelta < -1e-4f) _expectedClosedCells++;
                if (newExpected > 0.5f && _postApplyLc.Capacity[index] <= 1e-4f) _falseRemainingSolids++;
                if (newExpected <= 1e-4f && _postApplyLc.Capacity[index] > 0.5f) _falseOpenings++;
                if (Math.Abs(expectedDelta) > 0.05f && Math.Abs(actualDelta) <= 1e-4f) _missedChangedCells++;
                if (Math.Abs(expectedDelta) <= 0.01f && Math.Abs(actualDelta) > 0.05f) _extraChangedCells++;
            }
        }

        private int SelectTraceParticle(VolumetricFlipDomain.FluidParticle[] particles)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].Alive == 0) continue;
                Vector3 world = particles[i].Position + _domain.WorldOrigin;
                float dx = Mathf.Max(0f, Mathf.Max(_editBounds.min.x - world.x, world.x - _editBounds.max.x));
                float dz = Mathf.Max(0f, Mathf.Max(_editBounds.min.z - world.z, world.z - _editBounds.max.z));
                float dy = Mathf.Max(0f, world.y - _editBounds.max.y);
                float distance = dx * dx + dz * dz + 0.05f * dy * dy;
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        private void WriteStageTrace()
        {
            IReadOnlyList<Vector4[]> trace = _domain.FlipDomain.LastStepTrace;
            IReadOnlyList<Vector4[]> stencil = _domain.FlipDomain.LastStepStencilTrace;
            string[] names = { "post-p2g", "post-mac-projection", "post-g2p", "post-advection", "post-collision" };
            var rows = new List<string>();
            for (int stage = 0; stage < trace.Count; stage++)
            for (int slot = 0; slot < trace[stage].Length; slot++)
            {
                Vector4 v = trace[stage][slot];
                rows.Add(stage + "," + (stage < names.Length ? names[stage] : "stage-" + stage) + ",trace," + slot + "," + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w));
            }
            for (int stage = 0; stage < stencil.Count; stage++)
            for (int slot = 0; slot < stencil[stage].Length; slot++)
            {
                Vector4 v = stencil[stage][slot];
                rows.Add(stage + "," + (stage < names.Length ? names[stage] : "stage-" + stage) + ",stencil," + slot + "," + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w));
            }
            File.WriteAllLines(Path.Combine(_directory, "fluid-stage-trace.csv"), HeaderPlus("stage,stageName,kind,slot,x,y,z,w", rows));
        }

        private void WriteFluidSummary(LcSnapshot snapshot, string label)
        {
            VolumetricWaterSettings settings = _domain.MacDomain.Settings;
            double volume = 0.0, newRegionVolume = 0.0, comY = 0.0, potential = 0.0, speedSum = 0.0, maxSpeed = 0.0;
            int alive = 0, particlesInNew = 0;
            for (int i = 0; i < snapshot.Particles.Length; i++)
            {
                VolumetricFlipDomain.FluidParticle p = snapshot.Particles[i];
                if (p.Alive == 0 || p.Volume <= 0f) continue;
                alive++;
                Vector3 world = p.Position + _domain.WorldOrigin;
                double speed = p.Velocity.magnitude;
                volume += p.Volume;
                comY += p.Volume * world.y;
                potential += p.Volume * 9.81 * world.y;
                speedSum += speed;
                if (speed > maxSpeed) maxSpeed = speed;
                int cell = PositionCell(p.Position, settings);
                if (_newlyAccessibleCells.Contains(cell)) { newRegionVolume += p.Volume; particlesInNew++; }
            }
            double surfaceVariance = SurfaceVariance(snapshot.Liquid, settings);
            string path = Path.Combine(_directory, "fluid-response.csv");
            if (!File.Exists(path)) File.WriteAllText(path, "label,simSeconds,volume,newRegionVolume,particlesInNew,comY,potentialEnergy,meanSpeed,maxSpeed,surfaceVariance,captureMs,pressureResidual,iterations\r\n");
            File.AppendAllText(path, label + "," + snapshot.SimulatedSeconds.ToString("F6", Invariant) + "," + volume.ToString("F9", Invariant) + "," +
                newRegionVolume.ToString("F9", Invariant) + "," + particlesInNew + "," + (volume > 0 ? comY / volume : 0).ToString("F9", Invariant) + "," +
                potential.ToString("F9", Invariant) + "," + (alive > 0 ? speedSum / alive : 0).ToString("F9", Invariant) + "," + maxSpeed.ToString("F9", Invariant) + "," +
                surfaceVariance.ToString("F9", Invariant) + "," + snapshot.CaptureMilliseconds.ToString("F3", Invariant) + "," +
                snapshot.Diagnostics.Mac.CutCells.PressureResidual.ToString("E9", Invariant) + "," + snapshot.Diagnostics.Mac.CutCells.PcgIterations + "\r\n");
        }

        private void WriteManifest(string phase)
        {
            if (string.IsNullOrEmpty(_directory)) return;
            string json = "{\n" +
                "  \"phase\": \"" + Escape(phase) + "\",\n" +
                "  \"protectedCheckpoint\": \"2a1eb48f88f1accb40bcaa0a4f3891a76e4506b6\",\n" +
                "  \"captureUtc\": \"" + DateTime.UtcNow.ToString("O", Invariant) + "\",\n" +
                "  \"sourceId\": \"" + Escape(_dnaSource ?? (_editedAfter != null ? _editedAfter.SourceId : "")) + "\",\n" +
                "  \"operation\": \"" + Escape(_operation ?? "") + "\",\n" +
                "  \"revisionBefore\": " + _dnaRevisionBefore + ",\n" +
                "  \"revisionAfter\": " + _dnaRevisionAfter + ",\n" +
                "  \"changedSamples\": " + _changedSamples + ",\n" +
                "  \"removedSolidVolumeM3\": " + _removedSolidVolume.ToString("F9", Invariant) + ",\n" +
                "  \"newAccessibleLcVolumeM3\": " + _newAccessibleVolume.ToString("F9", Invariant) + ",\n" +
                "  \"openedCells\": " + _openedCells + ",\n" +
                "  \"falseRemainingSolids\": " + _falseRemainingSolids + ",\n" +
                "  \"falseOpenings\": " + _falseOpenings + ",\n" +
                "  \"dnaDatabaseHit\": " + (_dnaDatabaseHit ? "true" : "false") + ",\n" +
                "  \"dnaAsset\": \"" + Escape(_dnaAsset ?? "") + "\",\n" +
                "  \"dnaCategory\": \"" + Escape(_dnaCategory ?? "") + "\",\n" +
                "  \"dnaChange\": \"" + Escape(_dnaChange ?? "") + "\",\n" +
                "  \"dnaRuntimeHandle\": " + _dnaRuntimeHandle + ",\n" +
                "  \"dnaDependencyHandle\": " + _dnaDependencyHandle + ",\n" +
                "  \"pceSensingMs\": " + ElapsedMs(_hostEventTicks, _pceReadyTicks).ToString("F6", Invariant) + ",\n" +
                "  \"readyToLcReceiveMs\": " + ElapsedMs(_pceReadyTicks, _lcReceiveTicks).ToString("F6", Invariant) + ",\n" +
                "  \"hostToLcAppliedMs\": " + ElapsedMs(_hostEventTicks, _lcAppliedTicks).ToString("F6", Invariant) + ",\n" +
                "  \"geometryUpdate\": \"" + Escape(_geometryUpdate ?? "") + "\"\n" +
                "}\n";
            File.WriteAllText(Path.Combine(_directory, "manifest.json"), json);
        }

        private void WriteCodyObservation()
        {
            string directory = Path.Combine(Paths.ConfigPath, "LiquidCore", "CODY");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "terrain-edit-observations-v1.jsonl");
            string status = _falseRemainingSolids == 0 && _falseOpenings == 0 && _missedChangedCells == 0 ? "validated" : "mismatched";
            string row = "{\"schema\":1,\"observedUtc\":\"" + DateTime.UtcNow.ToString("O", Invariant) + "\",\"gameBuild\":\"" +
                         Escape(PhysicalWaterPlugin.ValheimKnowledge?.Data?.valheim?.steamBuildId ?? "unknown") + "\",\"sourceClass\":\"Heightmap\",\"event\":\"terrain operation/regenerate\",\"recipe\":\"authoritative-local/immediate prepared heightfield\",\"status\":\"" + status +
                         "\",\"changedSamples\":" + _changedSamples + ",\"expectedOpenedCells\":" + _expectedOpenedCells +
                         ",\"actualOpenedCells\":" + _openedCells + ",\"falseRemainingSolids\":" + _falseRemainingSolids +
                         ",\"falseOpenings\":" + _falseOpenings + ",\"hostToLcAppliedMs\":" + ElapsedMs(_hostEventTicks, _lcAppliedTicks).ToString("F6", Invariant) + "}";
            File.AppendAllText(path, row + Environment.NewLine);
            File.WriteAllText(Path.Combine(_directory, "cody-observation.json"), row + Environment.NewLine);
        }

        private static double SurfaceVariance(float[] liquid, VolumetricWaterSettings s)
        {
            if (liquid == null || liquid.Length == 0) return 0.0;
            var heights = new List<double>();
            for (int z = 0; z < s.ResolutionZ; z++)
            for (int x = 0; x < s.ResolutionX; x++)
            {
                double column = 0.0;
                for (int y = 0; y < s.ResolutionY; y++) column += liquid[x + s.ResolutionX * (y + s.ResolutionY * z)] * s.CellSize;
                if (column > 1e-6) heights.Add(column);
            }
            if (heights.Count < 2) return 0.0;
            double mean = 0.0; for (int i = 0; i < heights.Count; i++) mean += heights[i]; mean /= heights.Count;
            double variance = 0.0; for (int i = 0; i < heights.Count; i++) { double d = heights[i] - mean; variance += d * d; }
            return variance / heights.Count;
        }

        private static int PositionCell(Vector3 p, VolumetricWaterSettings s)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x / s.CellSize), 0, s.ResolutionX - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(p.y / s.CellSize), 0, s.ResolutionY - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z / s.CellSize), 0, s.ResolutionZ - 1);
            return x + s.ResolutionX * (y + s.ResolutionY * z);
        }

        private static Vector3Int Cell(int index, int nx, int ny)
        {
            int x = index % nx; int q = index / nx; int y = q % ny; int z = q / ny;
            return new Vector3Int(x, y, z);
        }

        private static double PositiveDelta(float[] before, float[] after)
        {
            double sum = 0.0; int count = Math.Min(before.Length, after.Length);
            for (int i = 0; i < count; i++) if (after[i] > before[i]) sum += after[i] - before[i];
            return sum;
        }

        private static float SampleHeight(HeightSnapshot map, float worldX, float worldZ)
        {
            Vector3 local = map.Heightmap.transform.InverseTransformPoint(new Vector3(worldX, map.Origin.y, worldZ));
            float gx = Mathf.Clamp(local.x / map.Scale + 0.5f * map.Width, 0f, map.Side - 1f);
            float gz = Mathf.Clamp(local.z / map.Scale + 0.5f * map.Width, 0f, map.Side - 1f);
            int x0 = Math.Min(Mathf.FloorToInt(gx), map.Side - 2), z0 = Math.Min(Mathf.FloorToInt(gz), map.Side - 2);
            float tx = gx - x0, tz = gz - z0;
            float a = Mathf.Lerp(map.Heights[x0 + map.Side * z0], map.Heights[x0 + 1 + map.Side * z0], tx);
            float b = Mathf.Lerp(map.Heights[x0 + map.Side * (z0 + 1)], map.Heights[x0 + 1 + map.Side * (z0 + 1)], tx);
            return map.Origin.y + Mathf.Lerp(a, b, tz);
        }

        private static Vector3 HeightSampleWorld(HeightSnapshot map, int x, int z, float height)
        {
            return map.Heightmap.transform.TransformPoint(new Vector3((x - 0.5f * map.Width) * map.Scale, height, (z - 0.5f * map.Width) * map.Scale));
        }

        private static string Describe(TerrainOp.Settings m)
        {
            if (m == null) return "unknown";
            return "level=" + m.m_level + ",raise=" + m.m_raise + ",smooth=" + m.m_smooth +
                   ",levelRadius=" + F(m.m_levelRadius) + ",raiseRadius=" + F(m.m_raiseRadius) +
                   ",levelOffset=" + F(m.m_levelOffset) + ",raiseDelta=" + F(m.m_raiseDelta);
        }

        private static string HierarchyPath(Transform t)
        {
            if (t == null) return "null";
            var parts = new List<string>();
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse(); return string.Join("/", parts.ToArray());
        }

        private static string HashFloats(float[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static string[] HeaderPlus(string header, List<string> rows)
        {
            var result = new string[rows.Count + 1]; result[0] = header; rows.CopyTo(result, 1); return result;
        }

        private static string Escape(string value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        private static string F(float value) => value.ToString("R", Invariant);
        private static double ElapsedMs(long start, long end) => start > 0 && end >= start ? (end - start) * 1000.0 / Stopwatch.Frequency : 0.0;
    }
}
