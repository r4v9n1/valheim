using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using R4V9N1.PhysicalOcean.Cody;
using R4V9N1.PhysicalOcean.Probes;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class PhysicalWaterDeferredFillGate
    {
        internal bool Pending { get; private set; }

        internal void Queue()
        {
            Pending = true;
        }

        internal bool TryConsume(bool geometryReady)
        {
            if (!geometryReady || !Pending) return false;
            Pending = false;
            return true;
        }

        internal void Clear()
        {
            Pending = false;
        }
    }

    internal sealed class PhysicalWaterDevE1Runtime : MonoBehaviour
    {
        private sealed class LiveMvcTopology : ILiquidCoreResolvedCatchmentTopology
        {
            private readonly VolumetricCutCellCatchmentTopology _cutCells;
            private readonly ProbeColonyCatchmentTopology _pce;

            internal LiveMvcTopology(VolumetricFiniteDomainController domain, LiquidCorePceRuntime pceRuntime)
            {
                if (pceRuntime == null) throw new ArgumentNullException(nameof(pceRuntime));
                _cutCells = new VolumetricCutCellCatchmentTopology(
                    domain, 4096, pceRuntime.ClassifySubGridObstacle);
                VolumetricWaterSettings settings = domain.MacDomain.Settings;
                _pce = new ProbeColonyCatchmentTopology(
                    pceRuntime.World, new Vector3Int(32, 32, 32), settings.CellSize, domain.WorldOrigin);
            }

            public bool HasPhysicalOpenPath(LiquidCoreWaterBodyRecord source,
                LiquidCoreWaterBodyRecord destination, CodyDrainageExit exit) =>
                _cutCells.HasPhysicalOpenPath(source, destination, exit) ||
                _pce.HasPhysicalOpenPath(source, destination, exit);

            public bool IsSubGridSpillPlausible(LiquidCoreWaterBodyRecord source,
                LiquidCoreWaterBodyRecord destination, CodyDrainageExit exit) =>
                TryResolveSubGridSpill(source, destination, exit, out _);

            public bool TryResolveSubGridSpill(LiquidCoreWaterBodyRecord source,
                LiquidCoreWaterBodyRecord destination, CodyDrainageExit exit,
                out CodyDrainageExit resolved) =>
                _cutCells.TryResolveSubGridSpill(source, destination, exit, out resolved) ||
                _pce.TryResolveSubGridSpill(source, destination, exit, out resolved);
        }

        internal static PhysicalWaterDevE1Runtime Instance { get; private set; }

        private readonly List<GameObject> _geometryRoots = new List<GameObject>();
        private readonly List<GameObject> _activeGeometryRoots = new List<GameObject>();
        private readonly List<GameObject> _changedGeometryRoots = new List<GameObject>();
        private readonly List<int> _removedGeometryRootIds = new List<int>();
        private readonly Dictionary<int, VolumetricPreparedGeometryDescriptor> _changedPreparedGeometry =
            new Dictionary<int, VolumetricPreparedGeometryDescriptor>();
        private readonly Dictionary<int, int> _coverageGeometryRevisions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _appliedGeometryRevisions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _preparedGeometryRevisions = new Dictionary<int, int>();
        private readonly LiquidCoreNerveTelemetry _nerveTelemetry = new LiquidCoreNerveTelemetry();
        private AssetBundle _bundle;
        private ComputeShader _macShader;
        private ComputeShader _flipShader;
        private ComputeShader _surfaceShader;
        private Material _surfaceMaterial;
        private GameObject _domainObject;
        private VolumetricStreamingDomainController _streaming;
        private VolumetricFiniteDomainController _domain;
        private float _accumulator;
        private float _nextTelemetryTime;
        private bool _registeredCommands;
        private int _lastSubsteps;
        private float _lastSimulationCpuMs;
        private readonly float[] _telemetryFrameMilliseconds = new float[1024];
        private int _telemetryFrameCount;
        private int _telemetryFrameSampleCount;
        private int _telemetrySolverSteps;
        private double _telemetryFrameSum;
        private double _telemetryActiveCpuSum;
        private double _telemetrySolverSum;
        private double _telemetryPressureSum;
        private double _telemetryPcgSum;
        private double _telemetrySurfaceSum;
        private double _telemetryDeferredWallSum;
        private double _telemetryGpuReadbackWallSum;
        private double _telemetryWorkerQueueSum;
        private double _telemetryWorkerExecutionSum;
        private double _telemetryReadbackStagesSum;
        private int _telemetryPreviousOwnershipRequests;
        private long _telemetryPreviousOwnershipBytes;
        private int _telemetryPreviousGc0;
        private int _telemetryPreviousGc1;
        private int _telemetryPreviousGc2;
        private long _telemetryPreviousManagedMemory;
        private long _telemetryPreviousWallTimestamp;
        private double _telemetryPreviousSimulatedSeconds;
        private long _observedGeometryGeneration = -1;
        private int _appliedGeometryStateRevision = int.MinValue;
        private float _nextCausalGeometryApplyTime;
        private Vector3 _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private Bounds _geometryCoverageBounds;
        private VolumetricSolidGeometrySdf.RebasePreparation _initialGeometryPreparation;
        private long _preparedGeometryGeneration = -1;
        private int _preparedGeometryStateRevision = int.MinValue;
        private bool _preparedGeometryIsInitial;
        private readonly PhysicalWaterDeferredFillGate _deferredFill = new PhysicalWaterDeferredFillGate();
        private readonly PhysicalWaterOneHitTerrainTruth _oneHitTerrainTruth = new PhysicalWaterOneHitTerrainTruth();
        private VolumetricWaterSample _latestPlayerWaterSample;
        private bool _hasLatestPlayerWaterSample;
        private float _latestPlayerWaterSampleIssuedTime;
        private float _nextPlayerWaterQueryTime;
        private LiquidCoreMicroVolumeConsolidation _microVolumeConsolidation;
        private long _microVolumeGeometryRevision = long.MinValue;
        private long _completedSimulationSteps;
        private long _deferredMvcRefreshStep = -1;
        private readonly HashSet<ulong> _pendingMvcBodies = new HashSet<ulong>();
        private readonly HashSet<ulong> _deferredMvcBodies = new HashSet<ulong>();
        private readonly List<ulong> _mvcBodyScratch = new List<ulong>();
        private readonly List<LiquidCoreMicroSpillConnection> _microSpillScratch = new List<LiquidCoreMicroSpillConnection>();
        private readonly List<ulong> _retiredMicroSpillScratch = new List<ulong>();
        private LiquidCorePceRuntime _subscribedCodyRuntime;

        internal PhysicalWaterOneHitTerrainTruth OneHitTerrainTruth => _oneHitTerrainTruth;

        // Full field probes are intentionally explicit (F11) because their GPU
        // downloads serialize the render and simulation queues. Never schedule
        // them from the frame loop.

        private void Awake()
        {
            Instance = this;
            RegisterCommands();
            LoadAssets();
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 finite streaming test mode active. Global ocean replacement OFF. " +
                "Vanilla-water suppression OFF. Finite local-player liquid-level feed and presentation interaction ON; " +
                "ship/fish/object buoyancy replacement remains OFF. Explicit finite fluid only.");
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 explicit test controls: console pw_e3_* (pw_e1_* aliases retained); " +
                "F6=create domain, F7=initialize a 216m3 local basin waterline, F8=clear, F9=status, F10=pause, F11=raw/live field probe, F12=capture terrain fixture; " +
                "Ctrl+Shift+D/W/X/S/P/B/C mirror those actions, Ctrl+Shift+G enables safe terrain-test flight, " +
                "and Ctrl+Shift+J/K apply deterministic native terrain lower/raise operations.");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DestroyDomain();
            if (_surfaceMaterial != null) Destroy(_surfaceMaterial);
            _surfaceMaterial = null;
            if (_bundle != null) _bundle.Unload(false);
            _bundle = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6)) CreateDomainCommand(null);
            if (Input.GetKeyDown(KeyCode.F7)) FillBoxCommand(null);
            if (Input.GetKeyDown(KeyCode.F8)) ClearCommand(null);
            if (Input.GetKeyDown(KeyCode.F9)) StatusCommand(null);
            if (Input.GetKeyDown(KeyCode.F10)) PauseCommand(null);
            if (Input.GetKeyDown(KeyCode.F11)) ProbeCommand(null);
            if (Input.GetKeyDown(KeyCode.F12)) CaptureTerrainCommand(null);
            if (TestChordDown(KeyCode.D)) CreateDomainCommand(null);
            if (TestChordDown(KeyCode.W)) FillBoxCommand(null);
            if (TestChordDown(KeyCode.X)) ClearCommand(null);
            if (TestChordDown(KeyCode.S)) StatusCommand(null);
            if (TestChordDown(KeyCode.P)) PauseCommand(null);
            if (TestChordDown(KeyCode.B)) ProbeCommand(null);
            if (TestChordDown(KeyCode.C)) CaptureTerrainCommand(null);
            if (TestChordDown(KeyCode.G)) EnableSafeTerrainTestFlight();
            if (TestChordDown(KeyCode.J)) ApplyTerrainTestDelta(-1f, "lower");
            if (TestChordDown(KeyCode.K)) ApplyTerrainTestDelta(1f, "raise");
            if (_streaming == null || _domain == null || !_domain.Initialized) return;
            RequestPlayerWaterSample();

            float fixedDt = 1f / 30f;
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, fixedDt * 3f);
            int maxSubsteps = Mathf.Clamp(PhysicalWaterPlugin.Settings.StageE1MaxSubstepsPerFrame.Value, 1, 4);
            int substeps = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (!_domain.Paused && _streaming.HasDeferredStep && _streaming.TryCompleteDeferredStep())
            {
                substeps++;
                _completedSimulationSteps++;
                _oneHitTerrainTruth.OnStepCompleted();
                AdvanceMicroSpillConnections(fixedDt);
            }
            // Complete the current authoritative substep before testing the
            // PCE handoff. Checking only at the start of Update starved dynamic
            // geometry indefinitely because the deferred pipeline normally
            // remained occupied across frame boundaries.
            if (!_streaming.HasDeferredStep)
            {
                _oneHitTerrainTruth.TryArm(_domain);
                SynchronizeGeometryIfReady();
            }
            // The GPU field copy and CPU pressure graph no longer occupy one
            // uninterrupted Update. Begin one authoritative substep, render
            // while its CPU projection runs, then finalize it on a later frame.
            if (!_domain.Paused && !_streaming.HasDeferredStep && _accumulator >= fixedDt && substeps < maxSubsteps &&
                _streaming.BeginDeferredStep(fixedDt))
                _accumulator -= fixedDt;
            watch.Stop();
            _lastSubsteps = substeps;
            _lastSimulationCpuMs = (float)watch.Elapsed.TotalMilliseconds;
            AccumulateTelemetry(substeps);
            RequestDeferredMvcRefreshIfReady();

            if (Time.unscaledTime >= _nextTelemetryTime)
            {
                _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
                LogTelemetry();
            }

        }

        private void RequestPlayerWaterSample()
        {
            Player player = Player.m_localPlayer;
            if (player == null || Time.unscaledTime < _nextPlayerWaterQueryTime || _domain.MacDomain.WaterQueryPending) return;
            _nextPlayerWaterQueryTime = Time.unscaledTime + 0.05f;
            VolumetricFiniteDomainController requestedDomain = _domain;
            Vector3 position = player.transform.position;
            float issuedTime = Time.unscaledTime;
            requestedDomain.RequestWaterSamplesAsync(new[] { position }, samples =>
            {
                if (_domain != requestedDomain || samples == null || samples.Length != 1) return;
                _latestPlayerWaterSample = samples[0];
                _hasLatestPlayerWaterSample = true;
                _latestPlayerWaterSampleIssuedTime = issuedTime;
            });
        }

        internal bool TryGetLatestPlayerWaterSample(Vector3 position, out VolumetricWaterSample sample)
        {
            sample = _latestPlayerWaterSample;
            if (!_hasLatestPlayerWaterSample || _domain == null ||
                Time.unscaledTime - _latestPlayerWaterSampleIssuedTime > 0.35f)
                return false;
            float maximumHorizontalDisplacement = _domain.MacDomain.Settings.CellSize * 2f;
            return LiquidCorePlayerWaterAuthorityResolver.TryResolveFinite(
                sample, position, maximumHorizontalDisplacement, out _);
        }

        private static bool TestChordDown(KeyCode key)
        {
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return control && shift && Input.GetKeyDown(key);
        }

        private static void EnableSafeTerrainTestFlight()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater terrain-test flight requires a local player.");
                return;
            }

            Player.m_debugMode = true;
            player.SetGodMode(true);
            player.SetNoPlacementCost(true);
            if (!player.IsDebugFlying()) player.ToggleDebugFly();
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater terrain-test flight enabled: debugMode=True, god=True, noPlacementCost=True, debugFly=True.");
        }

        private static void ApplyTerrainTestDelta(float delta, string operation)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                PhysicalWaterPlugin.Log.LogWarning("PhysicalWater terrain test operation requires a local player.");
                return;
            }

            Vector3 point = player.transform.position + player.transform.forward * 2f;
            if (!Heightmap.GetHeight(point, out float groundHeight))
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    $"PhysicalWater terrain test {operation} found no loaded heightmap at ({point.x:F2}, {point.z:F2}).");
                return;
            }

            point.y = groundHeight;
            Heightmap heightmap = Heightmap.FindHeightmap(point);
            if (heightmap == null)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    $"PhysicalWater terrain test {operation} could not resolve the loaded heightmap at {point}.");
                return;
            }

            TerrainComp compiler = heightmap.GetAndCreateTerrainCompiler();
            if (compiler == null)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    $"PhysicalWater terrain test {operation} could not create the native terrain compiler at {point}.");
                return;
            }

            GameObject operationObject = new GameObject($"PhysicalWater terrain test {operation}");
            operationObject.SetActive(false);
            operationObject.transform.position = point;
            TerrainOp terrainOperation = operationObject.AddComponent<TerrainOp>();
            terrainOperation.m_settings.m_level = false;
            terrainOperation.m_settings.m_raise = true;
            terrainOperation.m_settings.m_raiseRadius = 2f;
            terrainOperation.m_settings.m_raisePower = 1f;
            terrainOperation.m_settings.m_raiseDelta = delta;
            terrainOperation.m_settings.m_smooth = false;
            terrainOperation.m_settings.m_paintCleared = false;
            compiler.ApplyOperation(terrainOperation);
            Destroy(operationObject);

            PhysicalWaterPlugin.Log.LogInfo(
                $"PhysicalWater deterministic native terrain {operation} requested: point=({point.x:F2}, {point.y:F2}, {point.z:F2}), " +
                $"radius=2.00m, delta={delta:F2}m, compilerOwner={compiler.IsOwner()}.");
        }

        private void LateUpdate()
        {
            if (_domain != null && PhysicalWaterPlugin.Settings.StageE1RenderSurface.Value)
            {
                const float fixedDt = 1f / 30f;
                _domain.RenderSurface(Mathf.Clamp01(_accumulator / fixedDt));
            }
        }

        private void RegisterCommands()
        {
            if (_registeredCommands) return;
            _registeredCommands = true;
            new Terminal.ConsoleCommand("pw_e1_create_domain", "create fixed finite PhysicalWater E1 domain here", CreateDomainCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_fill_box", "spawn explicit finite water box: [x y z] [bottomOffset]", FillBoxCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_clear", "clear all E1 fluid", ClearCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_reset", "clear E1 fluid and refresh the fixed domain solids", ResetCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_status", "print E1 finite-domain diagnostics", StatusCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_pause", "toggle E1 simulation pause", PauseCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_step", "advance paused E1 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_probe", "capture one blocking raw/fraction/presentation field probe", ProbeCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e1_snapshot", "save a replayable finite-fluid snapshot", SnapshotCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_create_domain", "create finite PhysicalWater E3 streaming window here", CreateDomainCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_fill_box", "spawn explicit finite water box: [x y z] [bottomOffset]", FillBoxCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_clear", "clear all E3 fluid", ClearCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_reset", "clear E3 fluid and refresh streamed solids", ResetCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_status", "print E3 streaming diagnostics", StatusCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_pause", "toggle E3 simulation pause", PauseCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_step", "advance paused E3 simulation by one 1/30 second step", StepCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_probe", "capture one blocking raw/fraction/presentation field probe", ProbeCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_snapshot", "save a replayable finite-fluid snapshot", SnapshotCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_e3_capture_terrain", "capture replayable Valheim terrain: [width] [depth] [spacing] [label]", CaptureTerrainCommand, false, false, false, false, true);
            new Terminal.ConsoleCommand("pw_terrain_capture", "capture replayable Valheim terrain: [width] [depth] [spacing] [label]", CaptureTerrainCommand, false, false, false, false, true);
        }

        private void CreateDomainCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, true)) return;
            if (_macShader == null || _flipShader == null || _surfaceShader == null)
            {
                Reply(args, "E1 asset bundle is incomplete; domain was not created.");
                return;
            }

            var createWatch = System.Diagnostics.Stopwatch.StartNew();
            DestroyDomain();
            Vector3 playerPosition = Player.m_localPlayer.transform.position;
            const float dx = 0.75f;
            // The production compact window is 3 x 3 regions at 24 m per
            // region. Keep the requested origin centered on the actual 72 m
            // square; the former 48 m Z value survived from the old 3 x 2
            // window and biased every live domain 12 m forward.
            Vector3 worldSize = new Vector3(72f, 18f, 72f);
            Vector3 origin = new Vector3(
                Snap(playerPosition.x - worldSize.x * 0.5f, dx),
                Snap(playerPosition.y - 3f, dx),
                Snap(playerPosition.z - worldSize.z * 0.5f, dx));

            _domainObject = new GameObject("R4V9N1_PhysicalWaterDevE3StreamingDomain");
            DontDestroyOnLoad(_domainObject);
            _streaming = _domainObject.AddComponent<VolumetricStreamingDomainController>();
            _streaming.Initialize(
                _macShader,
                _flipShader,
                _surfaceShader,
                _surfaceMaterial,
                origin,
                new VolumetricFiniteDomainSettings
                {
                    // Per-cell pre/post divergence reconstruction is diagnostic
                    // only; the PCG residual remains available in live telemetry.
                    EnableCutCellDivergenceDiagnostics = false,
                    // LC's fixed-point cell ledger, not marker deposition, owns
                    // every cubic metre in the live finite-water domain.
                    UsePersistentGridVolumeAuthority = true,
                    PersistentGridMarkerMaintenanceIntervalSteps = 30
                },
                new VolumetricStreamingSettings
                {
                    RegionCellsX = 32,
                    RegionCellsZ = 32,
                    WindowRegionsX = 3,
                    // The live finite-water probe can spread across three 32-cell
                    // Z regions before settling. Keep one additional row in the
                    // compact window so that normal transport does not hit an
                    // artificial boundary while the safety gate remains strict.
                    WindowRegionsZ = 3,
                    PrefetchCells = 16,
                    // The 12 m prefetch guard permits a one-second ownership
                    // cadence. Faster full-particle GPU readbacks eventually
                    // serialized the live compute queue (captured at 66-149 ms).
                    OwnershipScanIntervalSteps = 30,
                    AutoRebaseWindow = true,
                    EnableBlockingValidationTelemetry = false,
                    UseAsyncOwnershipReadback = true
                });
            double initializeMs = createWatch.Elapsed.TotalMilliseconds;
            _domain = _streaming.Domain;
            _domain.WaterBodyPublisher.Published += OnWaterBodyRegistryPublished;
            BindCodyRuntime(LiquidCorePceRuntime.Instance);
            ReplayCompleteInitialWorldDomainIfAvailable();
            _accumulator = 0f;
            // Do not issue a blocking full-field diagnostic readback in the
            // same frame as F6 resource creation. The newly-created domain is
            // deliberately paused and empty; its first full telemetry sample
            // can wait for the normal interval.
            _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
            ResetTelemetryWindow(_streaming.Diagnostics, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalMemory(false));
            _observedGeometryGeneration = -1;
            _appliedGeometryStateRevision = int.MinValue;
            _nextCausalGeometryApplyTime = 0f;
            _appliedGeometryRevisions.Clear();
            _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            _domain.Paused = true;
            PhysicalWaterPersistenceRuntime.TryRestore(_streaming);
            double restoreMs = createWatch.Elapsed.TotalMilliseconds - initializeMs;
            EnsureGeometryCoverage();
            SynchronizeGeometryIfReady();
            double coverageMs = createWatch.Elapsed.TotalMilliseconds - initializeMs - restoreMs;
            VolumetricWaterSettings macSettings = _domain.MacDomain.Settings;
            string message = "E3 streaming window created: origin=" + Format(_domain.WorldOrigin) +
                             ", logicalRegion=" + (macSettings.CellSize * 32f).ToString("F0") + "x" + (macSettings.CellSize * 32f).ToString("F0") + "m" +
                             ", resolution=" + macSettings.ResolutionX + "x" + macSettings.ResolutionY + "x" + macSettings.ResolutionZ +
                             ", cell=" + macSettings.CellSize.ToString("F2") + "m, size=" + Format(_domain.WorldSize) +
                             ", particles=0. No sea-level or vanilla-water fill was performed.";
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 " + message);
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 domain creation timing: " +
                "initializeMs=" + initializeMs.ToString("F3", CultureInfo.InvariantCulture) +
                ", macWarmupMs=" + _domain.MacDomain.ComputeWarmupMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                ", flipWarmupMs=" + _domain.FlipDomain.ComputeWarmupMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                ", persistenceRestoreMs=" + restoreMs.ToString("F3", CultureInfo.InvariantCulture) +
                ", coverageGateMs=" + coverageMs.ToString("F3", CultureInfo.InvariantCulture) +
                ", totalMs=" + createWatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + ".");
            Reply(args, message);
        }

        private void FillBoxCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            if (_appliedGeometryStateRevision == int.MinValue)
            {
                if (args == null || args.Length <= 1)
                {
                    _deferredFill.Queue();
                    const string queued = "E1 geometry coverage is still preparing; the 216m3 fill request is queued and will run once geometry is ready.";
                    Reply(args, queued);
                    PhysicalWaterPlugin.Log.LogInfo("PW_E3_F7_QUEUED geometryReady=False, pendingDefaultFill=True.");
                    if (Player.m_localPlayer != null)
                        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, queued);
                }
                else
                {
                    Reply(args, "E1 geometry coverage is still preparing; explicit parameterized fill was not consumed. Retry after PW_E3_F6_READY.");
                }
                return;
            }
            Vector3 worldPlayer = Player.m_localPlayer != null
                ? Player.m_localPlayer.transform.position
                : _domain.WorldOrigin + _domain.WorldSize * 0.5f;
            if (args == null || args.Length <= 1)
            {
                const float requestedVolume = 216f;
                Bounds domainBounds = _domain.WorldBounds;
                var search = new Bounds(
                    new Vector3(worldPlayer.x, domainBounds.center.y, worldPlayer.z),
                    new Vector3(24f, domainBounds.size.y, 24f));
                float volume = _streaming.SeedWorldWaterline(search, requestedVolume, out int waterlineParticles);
                string waterlineMessage = "E3 terrain-aware waterline fill complete: search=24x" +
                    domainBounds.size.y.ToString("F1", CultureInfo.InvariantCulture) + "x24m, requestedVolume=" +
                    requestedVolume.ToString("F3", CultureInfo.InvariantCulture) + "m3, seededVolume=" +
                    volume.ToString("F3", CultureInfo.InvariantCulture) + "m3, particles=" + waterlineParticles +
                    ". Lowest terrain-open cells were selected by waterline; no elevated drop or reseeding source was used.";
                PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 " + waterlineMessage);
                ResetTelemetryWindow(_streaming.Diagnostics, GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalMemory(false));
                Reply(args, waterlineMessage);
                return;
            }

            float sx = ParsePositive(args, 1, 6f);
            float sy = ParsePositive(args, 2, 6f);
            float sz = ParsePositive(args, 3, 6f);
            float bottomOffset = Parse(args, 4, 4f);
            Vector3 center = new Vector3(worldPlayer.x, worldPlayer.y + bottomOffset + sy * 0.5f, worldPlayer.z);
            Bounds requested = new Bounds(center, new Vector3(sx, sy, sz));
            int count = _streaming.SeedWorldBox(requested);
            string message = "E3 explicit fill complete: requested=" + Format(requested.size) + ", worldCenter=" + Format(requested.center) + ", particles=" + count + ". No fallback or reseeding source was used.";
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 " + message);
            Reply(args, message);
        }

        private void ClearCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            _deferredFill.Clear();
            _streaming.ClearFluid();
            Reply(args, "E3 fluid cleared. particles=0, initialVolume=0, streaming window remains allocated.");
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 fluid cleared explicitly; no automatic refill is enabled.");
        }

        private void ResetCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            _streaming.ClearFluid();
            _domain.Paused = false;
            _accumulator = 0f;
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (adapter != null) adapter.MarkAllDirty();
            Reply(args, "E3 reset complete. Fluid is empty; geometry refresh requested; streaming state remains explicit.");
        }

        private void StatusCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            VolumetricFiniteDomainDiagnostics diagnostics = _domain.CaptureDiagnosticsSync();
            VolumetricStreamingDiagnostics streaming = _streaming.CaptureDiagnosticsSync();
            string line = "E3 status: finite=(" + diagnostics + "), streaming=(" + streaming + ")";
            Reply(args, line);
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 " + line);
            if (args == null && Player.m_localPlayer != null)
            {
                Player.m_localPlayer.Message(
                    MessageHud.MessageType.TopLeft,
                    "PhysicalWater E3 status logged: particles=" + streaming.TotalParticles +
                    ", volume=" + streaming.TotalVolume.ToString("F2") + "m3, crossings=" + streaming.SeamCrossings + ".");
            }
        }

        private void PauseCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            _domain.Paused = !_domain.Paused;
            Reply(args, "E3 paused=" + _domain.Paused + ".");
        }

        private void StepCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            bool paused = _domain.Paused;
            _domain.Paused = false;
            _streaming.Step(1f / 30f);
            _domain.Paused = paused;
            Reply(args, "E3 advanced by one 1/30 second step; paused=" + _domain.Paused + ".");
        }

        private void ProbeCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            if (_domain == null || _domain.ParticleCount <= 0)
            {
                Reply(args, "E3 live-field probe requires explicitly spawned fluid.");
                return;
            }

            CaptureLiveFieldProbe(args == null ? "manual-F11" : "manual-console");
            Reply(args, "E3 live-field probe captured. See BepInEx log and PhysicalWater\\Diagnostics CSV.");
        }

        private void SnapshotCommand(Terminal.ConsoleEventArgs args)
        {
            if (!ReadyForCommand(args, false)) return;
            try
            {
                string diagnosticsDirectory = Path.Combine(
                    Path.GetDirectoryName(typeof(PhysicalWaterPlugin).Assembly.Location),
                    "Diagnostics");
                string path = Path.Combine(
                    diagnosticsDirectory,
                    "FluidState-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".pwfs");
                VolumetricFluidStateSnapshot snapshot = VolumetricFluidStateSnapshot.Capture(_domain);
                snapshot.Save(path);
                Reply(args, "E3 fluid-state snapshot saved: " + path);
                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_SNAPSHOT path=" + path + " particles=" + snapshot.Particles.Length +
                    " cells=" + snapshot.SolidSdf.Length + ".");
            }
            catch (Exception exception)
            {
                PhysicalWaterPlugin.Log.LogError("PW_E3_SNAPSHOT failed: " + exception);
                Reply(args, "E3 fluid-state snapshot failed; see BepInEx log.");
            }
        }

        private void CaptureTerrainCommand(Terminal.ConsoleEventArgs args)
        {
            if (!PhysicalWaterPlugin.Settings.StageE1Enabled.Value || Player.m_localPlayer == null)
            {
                Reply(args, "Terrain capture requires Stage E finite mode and a local player.");
                return;
            }
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (adapter == null)
            {
                Reply(args, "Valheim terrain adapter is not available.");
                return;
            }

            float width = Mathf.Clamp(Parse(args, 1, 48f), 3f, 128f);
            float depth = Mathf.Clamp(Parse(args, 2, 48f), 3f, 128f);
            float spacing = Mathf.Clamp(Parse(args, 3, 1f), 0.25f, 4f);
            string label = args != null && args.Length > 4 ? SanitizeFileName(args[4]) : "terrain";
            Vector3 player = Player.m_localPlayer.transform.position;
            Bounds bounds = new Bounds(
                new Vector3(player.x, player.y, player.z),
                new Vector3(width, 512f, depth));
            if (!adapter.TryCaptureTerrainFixture(bounds, spacing, label, out VolumetricTerrainFixture fixture, out string error))
            {
                PhysicalWaterPlugin.Log.LogError("PW_TERRAIN_CAPTURE failed: " + error);
                Reply(args, "Terrain capture failed: " + error);
                return;
            }

            string directory = Path.Combine(
                Path.GetDirectoryName(typeof(PhysicalWaterPlugin).Assembly.Location),
                "Diagnostics",
                "TerrainFixtures");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(
                directory,
                label + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".pwterrain.json");
            File.WriteAllText(path, fixture.ToJson(true));
            string message = "Terrain fixture captured: path=" + path +
                             ", samples=" + fixture.SamplesX + "x" + fixture.SamplesZ +
                             ", spacing=" + fixture.SampleSpacing.ToString("F3", CultureInfo.InvariantCulture) +
                             "m, sources=" + fixture.SourceIds.Length +
                             ", bounds=" + fixture.WorldBounds + ".";
            PhysicalWaterPlugin.Log.LogInfo("PW_TERRAIN_CAPTURE " + message);
            Reply(args, message);
        }

        private void CaptureLiveFieldProbe(string reason)
        {
            try
            {
                VolumetricWaterDomain mac = _domain.MacDomain;
                VolumetricSurfaceReconstructor surface = _domain.Surface;
                if (mac == null || surface == null || !surface.Ready)
                {
                    PhysicalWaterPlugin.Log.LogWarning("PW_E3_PROBE skipped: MAC/surface not ready.");
                    return;
                }

                float[] fractions = mac.CaptureLiquidFractionsSync();
                float[] interfaceTops = mac.CaptureLiquidInterfaceTopSync();
                float[] solidSdf = mac.CaptureSolidSdfSync();
                float[] columnHeights = surface.CaptureColumnHeightsSync();
                float[] columnCoverages = surface.CaptureColumnCoveragesSync();
                if (fractions.Length != mac.CellCount || interfaceTops.Length != mac.CellCount || solidSdf.Length != mac.CellCount ||
                    columnHeights.Length != surface.SurfaceResolutionX * surface.SurfaceResolutionZ ||
                    columnCoverages.Length != surface.SurfaceResolutionX * surface.SurfaceResolutionZ)
                {
                    PhysicalWaterPlugin.Log.LogError(
                        "PW_E3_PROBE failed readback sizes: fractions=" + fractions.Length +
                        ", interfaceTops=" + interfaceTops.Length +
                        ", sdf=" + solidSdf.Length + ", columns=" + columnHeights.Length +
                        ", coverages=" + columnCoverages.Length + ".");
                    return;
                }

                ComputeBuffer currentBuffer = surface.RenderSurfaceVertexBuffer;
                ComputeBuffer previousBuffer = surface.PreviousRenderSurfaceVertexBuffer;
                VolumetricSurfaceReconstructor.SurfaceVertex[] currentVertices = Array.Empty<VolumetricSurfaceReconstructor.SurfaceVertex>();
                VolumetricSurfaceReconstructor.SurfaceVertex[] previousVertices = Array.Empty<VolumetricSurfaceReconstructor.SurfaceVertex>();
                if (currentBuffer != null)
                {
                    currentVertices = new VolumetricSurfaceReconstructor.SurfaceVertex[currentBuffer.count];
                    currentBuffer.GetData(currentVertices);
                }
                if (previousBuffer != null)
                {
                    previousVertices = new VolumetricSurfaceReconstructor.SurfaceVertex[previousBuffer.count];
                    previousBuffer.GetData(previousVertices);
                }

                VolumetricWaterSettings settings = mac.Settings;
                int nx = settings.ResolutionX;
                int ny = settings.ResolutionY;
                int nz = settings.ResolutionZ;
                float dx = settings.CellSize;
                int sx = surface.SurfaceResolutionX;
                int sz = surface.SurfaceResolutionZ;
                float sdx = surface.SurfaceCellSize;
                float alpha = Mathf.Clamp01(_accumulator / (1f / 30f));
                float sim = mac.Diagnostics.SimulatedSeconds;
                Vector3 origin = _domain.WorldOrigin;

                var allRaw = new List<float>();
                var allColumn = new List<float>();
                var allTerrain = new List<float>();
                var allRender = new List<float>();
                var rawTerrainA = new List<float>();
                var rawTerrainB = new List<float>();
                var columnTerrainA = new List<float>();
                var columnTerrainB = new List<float>();
                var rawColumnA = new List<float>();
                var rawColumnB = new List<float>();
                var columnRenderA = new List<float>();
                var columnRenderB = new List<float>();
                var representative = new List<string>();
                var csv = new List<string>(sx * sz + 2)
                {
                    "reason,simSeconds,surfaceX,surfaceZ,worldX,worldZ,terrainWorldY,solidSurfaceWorldY,legacyFractionTopWorldY,interfaceTopWorldY,columnHeightWorldY,columnCoverage,renderPreviousWorldY,renderCurrentWorldY,renderInterpolatedWorldY,topLiquidCell,topFraction,interfaceMinusTerrain,columnMinusInterface,renderMinusColumn"
                };

                int wetColumns = 0;
                for (int z = 0; z < sz; z++)
                for (int x = 0; x < sx; x++)
                {
                    float localX = (x + 0.5f) * sdx;
                    float localZ = (z + 0.5f) * sdx;
                    int liquidX = Mathf.Clamp(Mathf.FloorToInt(localX / dx), 0, nx - 1);
                    int liquidZ = Mathf.Clamp(Mathf.FloorToInt(localZ / dx), 0, nz - 1);

                    int topY = -1;
                    float topFraction = 0f;
                    for (int y = 0; y < ny; y++)
                    {
                        float fraction = Mathf.Clamp01(fractions[ProbeCellIndex(liquidX, y, liquidZ, nx, ny)]);
                        if (fraction <= 0f) continue;
                        topY = y;
                        topFraction = fraction;
                    }
                    if (topY < 0) continue;

                    wetColumns++;
                    int topIndex = ProbeCellIndex(liquidX, topY, liquidZ, nx, ny);
                    float legacyRawWorldY = origin.y + (topY + topFraction) * dx;
                    float rawLocalY = (topY + Mathf.Clamp01(interfaceTops[topIndex])) * dx;
                    float rawWorldY = origin.y + rawLocalY;
                    int columnIndex = x + sx * z;
                    float columnLocalY = columnHeights[columnIndex];
                    float columnCoverage = Mathf.Clamp01(columnCoverages[columnIndex]);
                    float columnWorldY = columnLocalY >= 0f ? origin.y + columnLocalY : float.NaN;

                    float worldX = origin.x + localX;
                    float worldZ = origin.z + localZ;
                    float terrainWorldY;
                    bool hasTerrain = Heightmap.GetHeight(
                        new Vector3(worldX, origin.y + _domain.WorldSize.y * 0.5f, worldZ),
                        out terrainWorldY);
                    if (!hasTerrain) terrainWorldY = float.NaN;

                    float solidLocalY = ProbeSolidSurfaceBelow(
                        solidSdf, liquidX, liquidZ, topY, nx, ny, dx);
                    float solidWorldY = ProbeFinite(solidLocalY) ? origin.y + solidLocalY : float.NaN;

                    float renderPreviousLocalY = ProbeRenderColumnY(previousVertices, sx, sz, x, z);
                    float renderCurrentLocalY = ProbeRenderColumnY(currentVertices, sx, sz, x, z);
                    float renderInterpolatedLocalY =
                        ProbeFinite(renderPreviousLocalY) && ProbeFinite(renderCurrentLocalY)
                            ? Mathf.Lerp(renderPreviousLocalY, renderCurrentLocalY, alpha)
                            : renderCurrentLocalY;
                    float renderPreviousWorldY = ProbeFinite(renderPreviousLocalY) ? origin.y + renderPreviousLocalY : float.NaN;
                    float renderCurrentWorldY = ProbeFinite(renderCurrentLocalY) ? origin.y + renderCurrentLocalY : float.NaN;
                    float renderWorldY = ProbeFinite(renderInterpolatedLocalY) ? origin.y + renderInterpolatedLocalY : float.NaN;

                    allRaw.Add(rawWorldY);
                    if (ProbeFinite(columnWorldY)) allColumn.Add(columnWorldY);
                    if (ProbeFinite(terrainWorldY)) allTerrain.Add(terrainWorldY);
                    if (ProbeFinite(renderWorldY)) allRender.Add(renderWorldY);

                    if (ProbeFinite(terrainWorldY))
                    {
                        rawTerrainA.Add(rawWorldY);
                        rawTerrainB.Add(terrainWorldY);
                        if (ProbeFinite(columnWorldY))
                        {
                            columnTerrainA.Add(columnWorldY);
                            columnTerrainB.Add(terrainWorldY);
                        }
                    }
                    if (ProbeFinite(columnWorldY))
                    {
                        rawColumnA.Add(rawWorldY);
                        rawColumnB.Add(columnWorldY);
                        if (ProbeFinite(renderWorldY))
                        {
                            columnRenderA.Add(columnWorldY);
                            columnRenderB.Add(renderWorldY);
                        }
                    }

                    string row =
                        reason + "," + ProbeFmt(sim) + "," + x + "," + z + "," +
                        ProbeFmt(worldX) + "," + ProbeFmt(worldZ) + "," +
                        ProbeFmt(terrainWorldY) + "," + ProbeFmt(solidWorldY) + "," +
                        ProbeFmt(legacyRawWorldY) + "," + ProbeFmt(rawWorldY) + "," + ProbeFmt(columnWorldY) + "," + ProbeFmt(columnCoverage) + "," +
                        ProbeFmt(renderPreviousWorldY) + "," + ProbeFmt(renderCurrentWorldY) + "," +
                        ProbeFmt(renderWorldY) + "," + topY + "," + ProbeFmt(topFraction) + "," +
                        ProbeFmt(ProbeFinite(terrainWorldY) ? rawWorldY - terrainWorldY : float.NaN) + "," +
                        ProbeFmt(ProbeFinite(columnWorldY) ? columnWorldY - rawWorldY : float.NaN) + "," +
                        ProbeFmt(ProbeFinite(renderWorldY) && ProbeFinite(columnWorldY) ? renderWorldY - columnWorldY : float.NaN);
                    csv.Add(row);

                    representative.Add(
                        "PW_E3_PROBE row sim=" + ProbeFmt(sim) +
                        " x=" + x + " z=" + z +
                        " terrainY=" + ProbeFmt(terrainWorldY) +
                        " solidY=" + ProbeFmt(solidWorldY) +
                        " legacyTopY=" + ProbeFmt(legacyRawWorldY) +
                        " interfaceTopY=" + ProbeFmt(rawWorldY) +
                        " columnY=" + ProbeFmt(columnWorldY) +
                        " coverage=" + ProbeFmt(columnCoverage) +
                        " renderY=" + ProbeFmt(renderWorldY) +
                        " topCell=" + topY +
                        " topFrac=" + ProbeFmt(topFraction));
                }

                string diagnosticsDirectory = Path.Combine(
                    Path.GetDirectoryName(typeof(PhysicalWaterPlugin).Assembly.Location),
                    "Diagnostics");
                Directory.CreateDirectory(diagnosticsDirectory);
                string safeReason = reason.Replace(" ", "_").Replace(":", "_").Replace("/", "_").Replace("\\", "_");
                string csvPath = Path.Combine(
                    diagnosticsDirectory,
                    "E3Probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                    "-" + safeReason + "-sim" +
                    sim.ToString("000.00", CultureInfo.InvariantCulture).Replace(".", "_") + ".csv");
                File.WriteAllLines(csvPath, csv);

                float corrRawTerrain = ProbeCorrelation(rawTerrainA, rawTerrainB);
                float corrColumnTerrain = ProbeCorrelation(columnTerrainA, columnTerrainB);
                float corrRawColumn = ProbeCorrelation(rawColumnA, rawColumnB);
                float corrColumnRender = ProbeCorrelation(columnRenderA, columnRenderB);
                float rmseRawColumn = ProbeRmse(rawColumnA, rawColumnB);
                float rmseColumnRender = ProbeRmse(columnRenderA, columnRenderB);

                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_PROBE summary reason=" + reason +
                    " sim=" + ProbeFmt(sim) +
                    " wetColumns=" + wetColumns +
                    " rawStd=" + ProbeFmt(ProbeStdDev(allRaw)) +
                    " columnStd=" + ProbeFmt(ProbeStdDev(allColumn)) +
                    " terrainStd=" + ProbeFmt(ProbeStdDev(allTerrain)) +
                    " renderStd=" + ProbeFmt(ProbeStdDev(allRender)) +
                    " corrRawTerrain=" + ProbeFmt(corrRawTerrain) +
                    " corrColumnTerrain=" + ProbeFmt(corrColumnTerrain) +
                    " corrRawColumn=" + ProbeFmt(corrRawColumn) +
                    " corrColumnRender=" + ProbeFmt(corrColumnRender) +
                    " rmseRawColumn=" + ProbeFmt(rmseRawColumn) +
                    " rmseColumnRender=" + ProbeFmt(rmseColumnRender) +
                    " presentationAlpha=" + ProbeFmt(alpha) +
                    " csv=" + csvPath + ".");

                int rowsToLog = Mathf.Min(25, representative.Count);
                for (int i = 0; i < rowsToLog; i++)
                {
                    int index = rowsToLog <= 1
                        ? 0
                        : Mathf.RoundToInt(i * (representative.Count - 1f) / (rowsToLog - 1f));
                    PhysicalWaterPlugin.Log.LogInfo(representative[index]);
                }
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogError("PW_E3_PROBE exception: " + ex);
            }
        }

        private static int ProbeCellIndex(int x, int y, int z, int nx, int ny)
        {
            return x + nx * (y + ny * z);
        }

        private static float ProbeSolidSurfaceBelow(
            float[] sdf, int x, int z, int topY, int nx, int ny, float dx)
        {
            int upper = Mathf.Clamp(topY, 0, ny - 1);
            for (int y = Mathf.Min(upper, ny - 2); y >= 0; y--)
            {
                float s0 = sdf[ProbeCellIndex(x, y, z, nx, ny)];
                float s1 = sdf[ProbeCellIndex(x, y + 1, z, nx, ny)];
                if (s0 <= 0f && s1 > 0f)
                {
                    float denominator = s1 - s0;
                    float t = Mathf.Abs(denominator) > 1e-6f ? Mathf.Clamp01(-s0 / denominator) : 0.5f;
                    return (y + 0.5f + t) * dx;
                }
            }

            for (int y = upper; y >= 0; y--)
            {
                if (sdf[ProbeCellIndex(x, y, z, nx, ny)] <= 0f)
                    return (y + 0.5f) * dx;
            }
            return float.NaN;
        }

        private static float ProbeRenderColumnY(
            VolumetricSurfaceReconstructor.SurfaceVertex[] vertices,
            int sx,
            int sz,
            int x,
            int z)
        {
            if (vertices == null || vertices.Length == 0 || sx < 2 || sz < 2) return float.NaN;
            int cellX = Mathf.Clamp(x, 0, sx - 2);
            int cellZ = Mathf.Clamp(z, 0, sz - 2);
            int cornerX = x - cellX;
            int cornerZ = z - cellZ;
            int offset;
            if (cornerX == 0 && cornerZ == 0) offset = 0;
            else if (cornerX == 0 && cornerZ == 1) offset = 1;
            else if (cornerX == 1 && cornerZ == 1) offset = 2;
            else offset = 5;

            int index = (cellX + (sx - 1) * cellZ) * 6 + offset;
            if (index < 0 || index >= vertices.Length) return float.NaN;
            return vertices[index].Position.y;
        }

        private static bool ProbeFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ProbeFmt(float value)
        {
            return ProbeFinite(value)
                ? value.ToString("F6", CultureInfo.InvariantCulture)
                : "NaN";
        }

        private static float ProbeCorrelation(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n < 2) return float.NaN;
            double meanA = 0.0, meanB = 0.0;
            for (int i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
            meanA /= n;
            meanB /= n;
            double covariance = 0.0, varianceA = 0.0, varianceB = 0.0;
            for (int i = 0; i < n; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                covariance += da * db;
                varianceA += da * da;
                varianceB += db * db;
            }
            double denominator = Math.Sqrt(varianceA * varianceB);
            return denominator > 1e-12 ? (float)(covariance / denominator) : float.NaN;
        }

        private static float ProbeStdDev(List<float> values)
        {
            if (values == null || values.Count < 2) return float.NaN;
            double mean = 0.0;
            for (int i = 0; i < values.Count; i++) mean += values[i];
            mean /= values.Count;
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                double d = values[i] - mean;
                sum += d * d;
            }
            return (float)Math.Sqrt(sum / values.Count);
        }

        private static float ProbeRmse(List<float> a, List<float> b)
        {
            int n = Mathf.Min(a.Count, b.Count);
            if (n == 0) return float.NaN;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double d = a[i] - b[i];
                sum += d * d;
            }
            return (float)Math.Sqrt(sum / n);
        }
        private void SynchronizeGeometryIfReady()
        {
            if (_streaming == null || _domain == null) return;
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (adapter == null) return;
            LiquidCorePceRuntime pce = LiquidCorePceRuntime.Instance;
            if (pce == null) return;
            EnsureGeometryCoverage();

            // After initial coverage, targeted PCE events cross as compact
            // source deltas. Do not wait for discovery polling or rebuild the
            // complete root set: the registered SDF owns unchanged sources.
            if (_appliedGeometryStateRevision != int.MinValue &&
                _domain.MacDomain.CanApplyPreparedSolidFields &&
                pce.TryConsumeCausalGeometrySignals(
                    _domain.WorldBounds,
                    _observedGeometryGeneration,
                    _changedGeometryRoots,
                    _removedGeometryRootIds,
                    out ProbeColonyCausalGeometryBatch causalBatch))
            {
                long applyStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                var applyWatch = System.Diagnostics.Stopwatch.StartNew();
                _changedPreparedGeometry.Clear();
                IReadOnlyList<ProbeColonyCausalGeometrySignal> drainedSignals = pce.LastDrainedCausalGeometrySignals;
                for (int i = 0; i < drainedSignals.Count; i++)
                {
                    ProbeColonyCausalGeometrySignal signal = drainedSignals[i];
                    if (signal.RootInstanceId == 0 || signal.PreparedGeometry == null) continue;
                    _changedPreparedGeometry[signal.RootInstanceId] = signal.PreparedGeometry;
                }
                bool oneHitApply = _oneHitTerrainTruth.BeforeLcGeometryApply(drainedSignals, causalBatch);
                VolumetricFiniteSolidUpdateDiagnostics causalUpdate = _streaming.SynchronizeGeometryDelta(
                    causalBatch.Generation,
                    _changedGeometryRoots,
                    _removedGeometryRootIds,
                    causalBatch.DirtyWorldBounds,
                    _changedPreparedGeometry,
                    synchronizeDiagnostics: false);
                if (oneHitApply) _oneHitTerrainTruth.AfterLcGeometryApply(causalUpdate);
                applyWatch.Stop();
                long solverReadyTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                double lcResponseMilliseconds = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    causalBatch.LatestReadyTimestamp,
                    solverReadyTimestamp);
                double totalNerveMilliseconds = ProbeColonyCausalGeometrySignalQueue.ElapsedMilliseconds(
                    causalBatch.EarliestEventTimestamp,
                    solverReadyTimestamp);
                Bounds sdfDependencyWorld = causalUpdate.Geometry.SdfDependencyLocalBounds;
                sdfDependencyWorld.center += _domain.WorldOrigin;
                Bounds cutCellDependencyWorld = causalUpdate.Geometry.CutCellDependencyLocalBounds;
                cutCellDependencyWorld.center += _domain.WorldOrigin;
                pce.MarkCausalGeometryApplied(
                    drainedSignals,
                    causalBatch.Generation,
                    sdfDependencyWorld,
                    cutCellDependencyWorld,
                    cutCellDependencyWorld,
                    sdfDependencyWorld,
                    _domain.MacDomain.LastSolidSdfUploadCells,
                    _domain.MacDomain.LastSolidCutCellUploadCells,
                    _domain.MacDomain.LastSolidApertureUploadFaces,
                    _domain.MacDomain.LastSolidUploadBytes);
                pce.QueueCodyCatchmentCoverage(_domain, _geometryCoverageBounds);
                _nerveTelemetry.RecordBatch(
                    drainedSignals,
                    applyStartTimestamp,
                    solverReadyTimestamp,
                    applyWatch.Elapsed.TotalMilliseconds,
                    causalUpdate,
                    _domain.MacDomain.LastSolidSdfUploadCells,
                    _domain.MacDomain.LastSolidCutCellUploadCells,
                    _domain.MacDomain.LastSolidApertureUploadFaces,
                    _domain.MacDomain.LastSolidUploadBytes);
                _observedGeometryGeneration = causalBatch.Generation;
                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_PCE_LC_NERVE geometryReady=True, generation=" + causalBatch.Generation +
                    ", signals=" + causalBatch.SignalCount +
                    ", changedRoots=" + causalBatch.AddedOrChangedRoots +
                    ", removedRoots=" + causalBatch.RemovedRoots +
                    ", T0toT1_PceSensingMs=" + causalBatch.PceSensingMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    ", T1toT2_LcResponseMs=" + lcResponseMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    ", T0toT2_TotalNerveMs=" + totalNerveMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    ", applyMs=" + applyWatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                    ", dirtyWorldBounds=" + causalBatch.DirtyWorldBounds + ": " + causalUpdate + ".");
                if (causalUpdate.ParticlesBefore > 0) RequestGeometrySafety(causalBatch.Generation);
                return;
            }
            if (!adapter.CausalGeometryCoverageReady) return;

            if (_initialGeometryPreparation != null)
            {
                // Preparing the full SDF can take several seconds, but the result cannot
                // be consumed before its worker completes. Rebuilding and validating the
                // complete causal root snapshot on every intervening frame only allocates,
                // hashes, and logs the same state hundreds of times. Validate once at the
                // application boundary instead; a generation/revision change is still
                // detected before the prepared fields can become authoritative.
                if (!_initialGeometryPreparation.IsCompleted) return;
                long currentGeneration;
                int currentStateRevision;
                if (!pce.TryGetCausalGeometrySnapshot(_domain.WorldBounds, -1, _activeGeometryRoots, null, out currentGeneration, out currentStateRevision) ||
                    currentGeneration != _preparedGeometryGeneration || currentStateRevision != _preparedGeometryStateRevision)
                {
                    PhysicalWaterPlugin.Log.LogInfo(
                        "PhysicalWater devE3 discarded stale " + (_preparedGeometryIsInitial ? "initial" : "dynamic") +
                        " geometry preparation generation=" + _preparedGeometryGeneration +
                        ", currentGeneration=" + currentGeneration + ".");
                    _initialGeometryPreparation = null;
                    _preparedGeometryGeneration = -1;
                    _preparedGeometryStateRevision = int.MinValue;
                    _preparedGeometryIsInitial = false;
                    _observedGeometryGeneration = -1;
                    return;
                }
                if (_initialGeometryPreparation.IsFaulted || _initialGeometryPreparation.IsCanceled)
                {
                    PhysicalWaterPlugin.Log.LogError(
                        "PhysicalWater devE3 " + (_preparedGeometryIsInitial ? "initial" : "dynamic") +
                        " geometry preparation failed before application.");
                    _initialGeometryPreparation = null;
                    _preparedGeometryIsInitial = false;
                    _observedGeometryGeneration = -1;
                    return;
                }

                var applyWatch = System.Diagnostics.Stopwatch.StartNew();
                bool wasInitialPreparation = _preparedGeometryIsInitial;
                if (!_streaming.TryApplyPreparedGeometry(
                        _preparedGeometryGeneration,
                        _initialGeometryPreparation,
                        out VolumetricFiniteSolidUpdateDiagnostics preparedUpdate,
                        synchronizeDiagnostics: wasInitialPreparation)) return;
                long appliedGeneration = _preparedGeometryGeneration;
                long appliedGeometryRevision = _preparedGeometryStateRevision;
                applyWatch.Stop();
                _appliedGeometryStateRevision = _preparedGeometryStateRevision;
                _appliedGeometryRevisions.Clear();
                foreach (KeyValuePair<int, int> pair in _preparedGeometryRevisions)
                    _appliedGeometryRevisions.Add(pair.Key, pair.Value);
                _initialGeometryPreparation = null;
                _preparedGeometryGeneration = -1;
                _preparedGeometryStateRevision = int.MinValue;
                _preparedGeometryIsInitial = false;
                _preparedGeometryRevisions.Clear();
                _domain.Paused = false;
                pce.DiscardCausalGeometrySignalsThrough(appliedGeneration);
                adapter.ReleaseCausalGeometryCoverage();
                _nextCausalGeometryApplyTime = 0f;
                PublishAppliedCapacityStorage(pce, appliedGeometryRevision);
                pce.QueueCodyCatchmentCoverage(_domain, _geometryCoverageBounds);
                string readyMarker = wasInitialPreparation ? "PW_E3_F6_READY" : "PW_E3_GEOMETRY_READY";
                PhysicalWaterPlugin.Log.LogInfo(
                    readyMarker + " geometryReady=True, fillReady=True, simulationPaused=False; prepared causal solid synchronization apply=" +
                    applyWatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms, initial=" +
                    wasInitialPreparation + ": " + preparedUpdate + ".");
                if (preparedUpdate.ParticlesBefore > 0) RequestGeometrySafety(appliedGeneration);
                if (_deferredFill.TryConsume(_appliedGeometryStateRevision != int.MinValue))
                {
                    PhysicalWaterPlugin.Log.LogInfo("PW_E3_F7_DEQUEUED geometryReady=True, pendingDefaultFill=False; executing preserved 216m3 request.");
                    FillBoxCommand(null);
                }
                return;
            }
            // PCE publishes only a completed causal generation. Consuming that
            // ready signal must not add an independent debounce delay: doing so
            // made terrain changes appear disconnected from their probe event.
            float now = Time.realtimeSinceStartup;
            if (now < _nextCausalGeometryApplyTime) return;
            long generation;
            int coverageStateRevision;
            if (!pce.TryGetCausalGeometrySnapshot(_geometryCoverageBounds, _observedGeometryGeneration, _geometryRoots, _coverageGeometryRevisions, out generation, out coverageStateRevision)) return;

            long activeGeneration;
            int stateRevision;
            if (!pce.TryGetCausalGeometrySnapshot(_domain.WorldBounds, -1, _activeGeometryRoots, null, out activeGeneration, out stateRevision) ||
                activeGeneration != generation) return;
            _observedGeometryGeneration = generation;
            if (!ShouldApplyCausalGeometry(generation, activeGeneration, stateRevision, _appliedGeometryStateRevision))
            {
                PhysicalWaterPlugin.Log.LogDebug(
                    "PhysicalWater devE3 skipped fringe-only causal geometry generation=" + generation +
                    ", activeStateRevision=" + stateRevision +
                    ", coverageStateRevision=" + coverageStateRevision +
                    ", activeRoots=" + _activeGeometryRoots.Count +
                    ", coverageRoots=" + _geometryRoots.Count + ".");
                return;
            }

            // The inactive solid bank may still be retiring the preceding
            // geometry generation. Defer without consuming PCE state; the
            // same causal snapshot is retried on a later frame once its CPU
            // synchronization fence reports safe reuse.
            if (_appliedGeometryStateRevision != int.MinValue && !_domain.MacDomain.CanApplyPreparedSolidFields) return;
            // PCE's retained source records and exact prepared descriptors are
            // the causal authority consumed below. The optional mapped probe
            // field has no LC solver consumer, so materializing every dormant
            // source through Physics.ClosestPoint here only stalls F6 without
            // changing occupancy, SDF, cut cells, or the geometry signal.
            _preparedGeometryRevisions.Clear();
            foreach (KeyValuePair<int, int> pair in _coverageGeometryRevisions)
                _preparedGeometryRevisions.Add(pair.Key, pair.Value);
            _preparedGeometryGeneration = generation;
            _preparedGeometryStateRevision = stateRevision;
            _preparedGeometryIsInitial = _appliedGeometryStateRevision == int.MinValue;
            if (!_preparedGeometryIsInitial && pce.TryGetReadyChangeBounds(
                    generation,
                    out Bounds dirtyWorldBounds,
                    out float pceEventToReadyMilliseconds,
                    out float pceReadyAgeMilliseconds))
            {
                _changedGeometryRoots.Clear();
                for (int i = 0; i < _geometryRoots.Count; i++)
                {
                    GameObject root = _geometryRoots[i];
                    if (root == null) continue;
                    int rootId = root.GetInstanceID();
                    if (!_coverageGeometryRevisions.TryGetValue(rootId, out int revision)) continue;
                    if (!_appliedGeometryRevisions.TryGetValue(rootId, out int appliedRevision) || appliedRevision != revision)
                        _changedGeometryRoots.Add(root);
                }

                var applyWatch = System.Diagnostics.Stopwatch.StartNew();
                VolumetricFiniteSolidUpdateDiagnostics incrementalUpdate = _streaming.SynchronizeGeometry(
                    generation,
                    _geometryRoots,
                    _changedGeometryRoots,
                    dirtyWorldBounds,
                    synchronizeDiagnostics: false);
                applyWatch.Stop();
                _appliedGeometryStateRevision = stateRevision;
                _appliedGeometryRevisions.Clear();
                foreach (KeyValuePair<int, int> pair in _coverageGeometryRevisions)
                    _appliedGeometryRevisions.Add(pair.Key, pair.Value);
                _preparedGeometryGeneration = -1;
                _preparedGeometryStateRevision = int.MinValue;
                _preparedGeometryIsInitial = false;
                _preparedGeometryRevisions.Clear();
                adapter.ReleaseCausalGeometryCoverage();
                _nextCausalGeometryApplyTime = 0f;
                PublishAppliedCapacityStorage(pce, stateRevision);
                pce.QueueCodyCatchmentCoverage(_domain, _geometryCoverageBounds);
                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_GEOMETRY_READY geometryReady=True, fillReady=True, simulationPaused=False; precise causal solid synchronization apply=" +
                    applyWatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms, changedRoots=" +
                    _changedGeometryRoots.Count + ", pceEventToReady=" +
                    pceEventToReadyMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms, pceReadyToApply=" +
                    pceReadyAgeMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms, dirtyWorldBounds=" +
                    dirtyWorldBounds + ": " + incrementalUpdate + ".");
                if (incrementalUpdate.ParticlesBefore > 0) RequestGeometrySafety(generation);
                return;
            }
            pce.PopulatePreparedGeometryByRoot(_geometryRoots, _changedPreparedGeometry);
            _initialGeometryPreparation = _streaming.BeginPrepareGeometry(
                generation,
                _geometryRoots,
                _changedPreparedGeometry);
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 began background " + (_preparedGeometryIsInitial ? "initial" : "dynamic") +
                " geometry preparation generation=" + generation + ", roots=" + _geometryRoots.Count +
                ", exactPreparedRoots=" + _changedPreparedGeometry.Count +
                (_preparedGeometryIsInitial ? ". Fill remains gated until atomic application." : ". Simulation continues against the preceding causal solid generation."));
        }

        private void PublishAppliedCapacityStorage(LiquidCorePceRuntime pce, long geometryRevision)
        {
            if (pce == null || _domain == null || pce.CodyL1 == null) return;
            if (!pce.CodyL1.TryLookup(_domain.WorldBounds.center, out CodyCatchmentDescriptor catchment))
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore PCE could not publish applied storage because no CODY catchment covers the active domain center.");
                return;
            }
            if (catchment.Validity != CodyCatchmentValidity.Valid ||
                !ContainsBounds(catchment.DependencyBounds, _domain.WorldBounds))
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore PCE deferred applied storage because the valid CODY catchment does not fully cover the active E3 grid.");
                return;
            }
            if (!pce.PublishAppliedDomainCapacityStorage(_domain.MacDomain, catchment, geometryRevision, out string error))
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore PCE applied storage publication deferred: " + error + ".");
        }

        private static bool ContainsBounds(Bounds outer, Bounds inner) =>
            outer.Contains(inner.min) && outer.Contains(inner.max);

        private void RequestGeometrySafety(long generation)
        {
            if (_domain == null) return;
            _domain.FlipDomain.RequestSafetyMetricsAsync(safety =>
            {
                if (_domain == null) return;
                bool unsafeState = !safety.Finite || safety.ParticlesInSolid != 0 || safety.ParticlesOutOfBounds != 0 ||
                                   float.IsNaN(safety.MaxParticleSpeed) || float.IsInfinity(safety.MaxParticleSpeed);
                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_GEOMETRY_SAFETY_ASYNC generation=" + generation + ", particles=" + safety.ParticleCount +
                    ", inSolid=" + safety.ParticlesInSolid + ", out=" + safety.ParticlesOutOfBounds +
                    ", maxSpeed=" + safety.MaxParticleSpeed.ToString("F4", CultureInfo.InvariantCulture) +
                    ", safe=" + (!unsafeState) + ".");
                if (!unsafeState) return;
                _domain.Paused = true;
                PhysicalWaterPlugin.Log.LogError("PhysicalWater devE3 paused after asynchronous geometry safety verification failed. No vanilla-water fallback was used.");
            });
        }

        internal static bool ShouldApplyCausalGeometry(
            long coverageGeneration,
            long activeGeneration,
            int activeStateRevision,
            int appliedStateRevision)
        {
            return coverageGeneration == activeGeneration && activeStateRevision != appliedStateRevision;
        }

        private void EnsureGeometryCoverage()
        {
            if (_domain == null || _domain.WorldOrigin == _coverageWindowOrigin) return;
            _coverageWindowOrigin = _domain.WorldOrigin;
            // A window rebase changes which cached sources are active even when
            // no Valheim object changed revision. Re-open the generation gate so
            // the new world/local mapping is compared and synchronized.
            _observedGeometryGeneration = -1;
            _geometryCoverageBounds = _domain.WorldBounds;
            Vector3 coverageSize = _geometryCoverageBounds.size;
            coverageSize.x += 48f;
            coverageSize.z += 48f;
            _geometryCoverageBounds.size = coverageSize;
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (adapter != null) adapter.RequestCausalGeometryCoverage(_geometryCoverageBounds);
            LiquidCorePceRuntime pce = LiquidCorePceRuntime.Instance;
            if (pce != null) pce.WarmCodyCatchments(_geometryCoverageBounds);
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 requested causal geometry coverage for current window plus one 24m logical-region margin: " + _geometryCoverageBounds + ".");
        }

        private void LogTelemetry()
        {
            VolumetricStreamingDiagnostics streaming = _streaming.Diagnostics;
            VolumetricWaterDomain mac = _domain.MacDomain;
            int frameCount = Math.Max(1, _telemetryFrameCount);
            int solverSteps = Math.Max(1, _telemetrySolverSteps);
            Array.Sort(_telemetryFrameMilliseconds, 0, _telemetryFrameSampleCount);
            float frameP95 = _telemetryFrameSampleCount > 0
                ? _telemetryFrameMilliseconds[Mathf.Clamp(Mathf.FloorToInt(0.95f * (_telemetryFrameSampleCount - 1)), 0, _telemetryFrameSampleCount - 1)]
                : 0f;
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            long managedMemory = GC.GetTotalMemory(false);
            VolumetricSolidGeometryUpdateDiagnostics geometry = _domain.Geometry.LastUpdate;
            long wallTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            double simulatedSeconds = mac != null ? mac.Diagnostics.SimulatedSeconds : 0.0;
            double realSeconds = _telemetryPreviousWallTimestamp > 0
                ? (wallTimestamp - _telemetryPreviousWallTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency
                : 0.0;
            double simulatedDelta = simulatedSeconds - _telemetryPreviousSimulatedSeconds;
            double simulationWallRatio = realSeconds > 1e-9 ? simulatedDelta / realSeconds : 0.0;
            double meanFrameMilliseconds = _telemetryFrameSum / frameCount;
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 nonblocking telemetry: paused=" + _domain.Paused +
                ", particles=" + _domain.ParticleCount +
                ", substeps=" + _lastSubsteps +
                ", activeCpuMs=" + _lastSimulationCpuMs.ToString("F3") +
                ", intervalFrames=" + _telemetryFrameCount +
                ", frameMs[mean/p95]=" + meanFrameMilliseconds.ToString("F3") + "/" + frameP95.ToString("F3") +
                ", fpsMean=" + (meanFrameMilliseconds > 1e-9 ? 1000.0 / meanFrameMilliseconds : 0.0).ToString("F2", CultureInfo.InvariantCulture) +
                ", throughput[real/sim/ratio]=" + realSeconds.ToString("F3", CultureInfo.InvariantCulture) + "/" +
                simulatedDelta.ToString("F3", CultureInfo.InvariantCulture) + "/" +
                simulationWallRatio.ToString("F3", CultureInfo.InvariantCulture) +
                ", simulationCpuMeanMs=" + (_telemetryActiveCpuSum / frameCount).ToString("F3") +
                ", solverStepMeanMs=" + (_telemetrySolverSum / solverSteps).ToString("F3") +
                ", pressureMeanMs=" + (_telemetryPressureSum / solverSteps).ToString("F3") +
                ", pcgMeanMs=" + (_telemetryPcgSum / solverSteps).ToString("F3") +
                ", surfaceMeanMs=" + (_telemetrySurfaceSum / solverSteps).ToString("F3") +
                ", deferredPipeline[wall/readback/queue/worker/stages]=" +
                (_telemetryDeferredWallSum / solverSteps).ToString("F3") + "/" +
                (_telemetryGpuReadbackWallSum / solverSteps).ToString("F3") + "/" +
                (_telemetryWorkerQueueSum / solverSteps).ToString("F3") + "/" +
                (_telemetryWorkerExecutionSum / solverSteps).ToString("F3") + "/" +
                (_telemetryReadbackStagesSum / solverSteps).ToString("F2") +
                ", projection[cells/downloadBytes/fallbacksTotal]=" +
                (mac != null ? mac.LastCutCellHotPathTimings.ProjectionCells.ToString(CultureInfo.InvariantCulture) : "0") + "/" +
                (mac != null ? (mac.LastCutCellHotPathTimings.DownloadFloats * sizeof(float)).ToString(CultureInfo.InvariantCulture) : "0") + "/" +
                (mac != null ? mac.ProjectionFallbackCount.ToString(CultureInfo.InvariantCulture) : "0") +
                ", ownership[requests/bytes]=" + (streaming.AsyncOwnershipRequests - _telemetryPreviousOwnershipRequests) + "/" + (streaming.OwnershipReadbackBytes - _telemetryPreviousOwnershipBytes) +
                ", managedMemory[bytes/delta]=" + managedMemory + "/" + (managedMemory - _telemetryPreviousManagedMemory) +
                ", gc[0/1/2]=" + (gc0 - _telemetryPreviousGc0) + "/" + (gc1 - _telemetryPreviousGc1) + "/" + (gc2 - _telemetryPreviousGc2) +
                ", regions[active/dormant]=" + streaming.ActiveRegions + "/" + streaming.DormantRegions +
                ", geometry[generation/changed/totalMs]=" + _domain.AppliedGeometryGeneration + "/" + geometry.ChangedCells + "/" + geometry.TotalMilliseconds.ToString("F3") +
                ", simulatedSeconds=" + (mac != null ? mac.Diagnostics.SimulatedSeconds.ToString("F3") : "n/a") +
                ", projectionCache=(" + (mac != null ? mac.LastProjectionCacheForensics.ToString() : "unavailable") + ")" +
                ", flipTiming=(" + _domain.FlipDomain.LastStepTimings + ")" +
                ", macTiming=(" + (mac != null ? mac.LastStepTimings.ToString() : "n/a") + ")" +
                ", streaming=(" + streaming + ")" +
                ", globalOcean=False, vanillaFallback=False, hiddenReseeding=False.");
            ResetTelemetryWindow(streaming, gc0, gc1, gc2, managedMemory);
        }

        private void AccumulateTelemetry(int substeps)
        {
            if (_telemetryFrameSampleCount < _telemetryFrameMilliseconds.Length)
                _telemetryFrameMilliseconds[_telemetryFrameSampleCount++] = Time.unscaledDeltaTime * 1000f;
            _telemetryFrameCount++;
            _telemetryFrameSum += Time.unscaledDeltaTime * 1000.0;
            _telemetryActiveCpuSum += _lastSimulationCpuMs;
            if (substeps <= 0 || _domain == null || _domain.MacDomain == null) return;
            VolumetricWaterDomain mac = _domain.MacDomain;
            _telemetrySolverSteps += substeps;
            _telemetrySolverSum += mac.LastStepTimings.TotalMilliseconds * substeps;
            _telemetryPressureSum += mac.LastStepTimings.PressureMilliseconds * substeps;
            _telemetryPcgSum += mac.LastCutCellHotPathTimings.Projection.PcgMilliseconds * substeps;
            _telemetrySurfaceSum += _domain.Surface.LastReconstructionMilliseconds * substeps;
            _telemetryDeferredWallSum += mac.LastCutCellHotPathTimings.DeferredWallMilliseconds * substeps;
            _telemetryGpuReadbackWallSum += mac.LastCutCellHotPathTimings.GpuReadbackWallMilliseconds * substeps;
            _telemetryWorkerQueueSum += mac.LastCutCellHotPathTimings.WorkerQueueMilliseconds * substeps;
            _telemetryWorkerExecutionSum += mac.LastCutCellHotPathTimings.WorkerExecutionMilliseconds * substeps;
            _telemetryReadbackStagesSum += mac.LastCutCellHotPathTimings.ReadbackStages * substeps;
        }

        private void ResetTelemetryWindow(VolumetricStreamingDiagnostics streaming, int gc0, int gc1, int gc2, long managedMemory)
        {
            _telemetryFrameCount = 0;
            _telemetryFrameSampleCount = 0;
            _telemetrySolverSteps = 0;
            _telemetryFrameSum = 0.0;
            _telemetryActiveCpuSum = 0.0;
            _telemetrySolverSum = 0.0;
            _telemetryPressureSum = 0.0;
            _telemetryPcgSum = 0.0;
            _telemetrySurfaceSum = 0.0;
            _telemetryDeferredWallSum = 0.0;
            _telemetryGpuReadbackWallSum = 0.0;
            _telemetryWorkerQueueSum = 0.0;
            _telemetryWorkerExecutionSum = 0.0;
            _telemetryReadbackStagesSum = 0.0;
            _telemetryPreviousOwnershipRequests = streaming.AsyncOwnershipRequests;
            _telemetryPreviousOwnershipBytes = streaming.OwnershipReadbackBytes;
            _telemetryPreviousGc0 = gc0;
            _telemetryPreviousGc1 = gc1;
            _telemetryPreviousGc2 = gc2;
            _telemetryPreviousManagedMemory = managedMemory;
            _telemetryPreviousWallTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            _telemetryPreviousSimulatedSeconds = _domain != null && _domain.MacDomain != null
                ? _domain.MacDomain.Diagnostics.SimulatedSeconds
                : 0.0;
        }

        private void LoadAssets()
        {
            string directory = Path.GetDirectoryName(typeof(PhysicalWaterPlugin).Assembly.Location);
            string path = string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "physicalwater_assets");
            if (path == null || !File.Exists(path))
            {
                PhysicalWaterPlugin.Log.LogError("PhysicalWater devE1 asset bundle is missing; commands remain available but cannot create a domain.");
                return;
            }

            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null)
            {
                PhysicalWaterPlugin.Log.LogError("PhysicalWater devE1 could not load its asset bundle.");
                return;
            }

            ComputeShader[] computeShaders = _bundle.LoadAllAssets<ComputeShader>();
            for (int i = 0; i < computeShaders.Length; i++)
            {
                ComputeShader shader = computeShaders[i];
                if (shader.name.IndexOf("VolumetricWater", StringComparison.OrdinalIgnoreCase) >= 0) _macShader = shader;
                else if (shader.name.IndexOf("VolumetricFlip", StringComparison.OrdinalIgnoreCase) >= 0) _flipShader = shader;
                else if (shader.name.IndexOf("VolumetricSurface", StringComparison.OrdinalIgnoreCase) >= 0) _surfaceShader = shader;
            }
            Material materialAsset = _bundle.LoadAsset<Material>("R4V9N1_PhysicalVolumetricDebugSurface");
            if (materialAsset != null) _surfaceMaterial = new Material(materialAsset) { name = "LiquidCore_CelWaterSurface" };
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE1 assets: mac=" + (_macShader != null) + ", flip=" + (_flipShader != null) + ", surface=" + (_surfaceShader != null) + ", celMaterial=" + (_surfaceMaterial != null) + ".");
        }

        private bool ReadyForCommand(Terminal.ConsoleEventArgs args, bool playerRequired)
        {
            if (!PhysicalWaterPlugin.Settings.StageE1Enabled.Value)
            {
                Reply(args, "Stage E1 is disabled in config.");
                return false;
            }
            if (playerRequired && Player.m_localPlayer == null)
            {
                Reply(args, "A local player is required to select the fixed E1 location.");
                return false;
            }
            if (!playerRequired && _domain == null)
            {
                Reply(args, "No E1 domain exists. Run pw_e1_create_domain first.");
                return false;
            }
            if (_streaming != null && _streaming.HasDeferredStep)
                _streaming.CompleteDeferredStepBlocking();
            return true;
        }

        private void BindCodyRuntime(LiquidCorePceRuntime pce)
        {
            if (pce == _subscribedCodyRuntime) return;
            if (_subscribedCodyRuntime != null)
                _subscribedCodyRuntime.CodyCatchmentPublished -= OnCodyCatchmentPublished;
            if (_subscribedCodyRuntime != null)
                _subscribedCodyRuntime.CompleteInitialWorldDomainPublished -= OnCompleteInitialWorldDomainPublished;
            _subscribedCodyRuntime = pce;
            if (_subscribedCodyRuntime != null)
            {
                _subscribedCodyRuntime.CodyCatchmentPublished += OnCodyCatchmentPublished;
                _subscribedCodyRuntime.CompleteInitialWorldDomainPublished += OnCompleteInitialWorldDomainPublished;
            }
        }

        private void ReplayCompleteInitialWorldDomainIfAvailable()
        {
            if (_subscribedCodyRuntime == null ||
                !_subscribedCodyRuntime.TryGetCompleteInitialWorldDomain(
                    out LiquidCoreInitialWorldWaterDomain domain)) return;
            OnCompleteInitialWorldDomainPublished(domain);
        }

        private void OnCompleteInitialWorldDomainPublished(LiquidCoreInitialWorldWaterDomain domain)
        {
            if (_streaming == null || _domain == null || !_domain.Initialized || domain == null)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore complete initial-water domain arrived before the E3 representation was ready; source commit deferred.");
                return;
            }
            try
            {
                // SeaLevel is used only as the named initial reference head.
                // PCE owns geometry/capacity; LiquidCore computes volume and
                // exact atoms; E3 receives only those supplied transactions.
                var rule = new LiquidCoreInitialWorldWaterSourceRule
                {
                    ReferenceHead = PhysicalWaterPlugin.Settings.SeaLevel.Value,
                    SourceCatchmentId = domain.SourceCatchmentId
                };
                LiquidCoreInitialWorldWaterSourcePlan plan = domain.ComputeSourcePlan(
                    rule, "valheim-ocean-initial-v1",
                    _domain.FlipDomain.ParticleVolumeAtomicScale);
                if (!_streaming.TryCommitInitialWorldWaterSourcePlan(plan, out string reason))
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore complete initial-water source commit deferred/fail-closed: " + reason + ".");
                    return;
                }
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore complete initial-water source committed: domain=" + domain.DomainId +
                    ", partitions=" + plan.Partitions.Length + ", volume=" +
                    plan.TotalSourceVolume.ToString("R", CultureInfo.InvariantCulture) +
                    ", atoms=" + plan.TotalSourceAtoms + ".");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore complete initial-water source calculation/commit failed closed: " + ex.Message + ".");
            }
        }

        private void OnWaterBodyRegistryPublished(long revision, LiquidCoreWaterBodyRefreshReason reason)
        {
            if (_domain == null || !_domain.Initialized) return;
            BindCodyRuntime(LiquidCorePceRuntime.Instance);
            IReadOnlyList<ulong> candidates = _domain.WaterBodies.LastMicroBodyCandidateIds;
            for (int i = 0; i < candidates.Count; i++)
            {
                ulong bodyId = candidates[i];
                if (!_domain.WaterBodies.Records.TryGetValue(bodyId, out LiquidCoreWaterBodyRecord body)) continue;
                if (body.AgeRevisions < 1)
                {
                    _deferredMvcBodies.Add(bodyId);
                    if (_deferredMvcRefreshStep < 0)
                        _deferredMvcRefreshStep = _completedSimulationSteps + 30;
                    continue;
                }
                _deferredMvcBodies.Remove(bodyId);
                EvaluateMicroBody(bodyId, reason.ToString());
            }
        }

        private void RequestDeferredMvcRefreshIfReady()
        {
            if (_domain == null || _deferredMvcBodies.Count == 0 || _deferredMvcRefreshStep < 0 ||
                _completedSimulationSteps < _deferredMvcRefreshStep || _domain.WaterBodyPublisher.Pending)
                return;
            _deferredMvcRefreshStep = -1;
            _domain.WaterBodyPublisher.RequestRefresh(LiquidCoreWaterBodyRefreshReason.Explicit);
        }

        private void OnCodyCatchmentPublished(CodyCatchmentDescriptor descriptor)
        {
            if (_domain == null || descriptor == null || _pendingMvcBodies.Count == 0) return;
            _mvcBodyScratch.Clear();
            foreach (ulong bodyId in _pendingMvcBodies) _mvcBodyScratch.Add(bodyId);
            for (int i = 0; i < _mvcBodyScratch.Count; i++)
            {
                ulong bodyId = _mvcBodyScratch[i];
                if (!_domain.WaterBodies.Records.TryGetValue(bodyId, out LiquidCoreWaterBodyRecord body))
                {
                    _pendingMvcBodies.Remove(bodyId);
                    continue;
                }
                if (_subscribedCodyRuntime == null ||
                    !_subscribedCodyRuntime.CodyL1.TryLookup(body.CenterOfMass, out CodyCatchmentDescriptor catchment) ||
                    catchment.CatchmentId != descriptor.CatchmentId)
                    continue;
                EvaluateMicroBody(bodyId, "CODY rebuild published");
            }
        }

        private void EvaluateMicroBody(ulong bodyId, string trigger)
        {
            if (_domain == null || !_domain.WaterBodies.Records.TryGetValue(bodyId, out LiquidCoreWaterBodyRecord body))
            {
                _pendingMvcBodies.Remove(bodyId);
                return;
            }
            if (_domain.MicroSpills.TryGet(bodyId, out LiquidCoreMicroSpillConnection existing) && existing.Valid)
            {
                _pendingMvcBodies.Remove(bodyId);
                return;
            }
            if (!EnsureMicroVolumeConsolidation())
            {
                _pendingMvcBodies.Add(bodyId);
                return;
            }
            LiquidCoreMicroVolumeEvaluation evaluation;
            try
            {
                evaluation = _microVolumeConsolidation.Evaluate(bodyId);
            }
            catch (Exception ex)
            {
                _pendingMvcBodies.Add(bodyId);
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore MVC evaluation preserved body " + bodyId + " after failure: " + ex.Message);
                return;
            }
            if (evaluation.Relation == LiquidCoreCatchmentRelation.PendingCatchment)
            {
                _pendingMvcBodies.Add(bodyId);
                if (_subscribedCodyRuntime != null)
                    _subscribedCodyRuntime.QueueCodyCatchmentCoverage(_domain, _geometryCoverageBounds);
            }
            else
            {
                _pendingMvcBodies.Remove(bodyId);
                if (_subscribedCodyRuntime != null &&
                    _subscribedCodyRuntime.CodyL1.TryLookup(body.CenterOfMass, out CodyCatchmentDescriptor catchment))
                    _domain.WaterBodies.SetCatchmentBinding(bodyId,
                        catchment.CatchmentId + ":" + catchment.GeometryRecipeRevision + ":" + catchment.DependencyRevisionHash);
            }
            PhysicalWaterPlugin.Log.LogInfo(
                "LiquidCore MVC event evaluation: trigger=" + trigger + ", source=" + bodyId +
                ", relation=" + evaluation.Relation + ", destination=" + evaluation.DestinationBodyId +
                ", catchment=" + evaluation.CatchmentId + ", conservedAtoms=" + body.VolumeAtoms +
                ", reason=" + evaluation.Reason + ".");
        }

        private bool EnsureMicroVolumeConsolidation()
        {
            LiquidCorePceRuntime pce = LiquidCorePceRuntime.Instance;
            if (_domain == null || pce == null || pce.CodyL1 == null) return false;
            if (_microVolumeConsolidation != null && _microVolumeGeometryRevision == _domain.AppliedGeometryGeneration)
                return true;
            var topology = new LiveMvcTopology(_domain, pce);
            VolumetricWaterSettings settings = _domain.MacDomain.Settings;
            _microVolumeConsolidation = new LiquidCoreMicroVolumeConsolidation(
                _domain.WaterBodies,
                pce.CodyL1,
                topology,
                _domain.WorldBounds,
                settings.CellSize,
                _domain.WorldOrigin,
                settings.ResolutionX,
                settings.ResolutionY,
                settings.ResolutionZ,
                minimumCandidateAge: 1,
                maximumCandidateSpeed: 0.25f,
                spillLedger: _domain.MicroSpills);
            _microVolumeGeometryRevision = _domain.AppliedGeometryGeneration;
            return true;
        }

        private void AdvanceMicroSpillConnections(float deltaTime)
        {
            if (_domain == null || _domain.MicroSpills.Count == 0 || !EnsureMicroVolumeConsolidation()) return;
            _microSpillScratch.Clear();
            foreach (LiquidCoreMicroSpillConnection connection in _domain.MicroSpills.Connections)
                _microSpillScratch.Add(connection);
            _retiredMicroSpillScratch.Clear();
            for (int i = 0; i < _microSpillScratch.Count; i++)
            {
                LiquidCoreMicroSpillConnection connection = _microSpillScratch[i];
                try
                {
                    _microVolumeConsolidation.AdvanceConnection(_domain, connection, deltaTime);
                }
                catch (Exception ex)
                {
                    PhysicalWaterPlugin.Log.LogWarning(
                        "LiquidCore MVC connector deferred without deleting water: source=" + connection.SourceBodyId +
                        ", inTransitAtoms=" + connection.InTransitAtoms + ", error=" + ex.Message);
                }
                if (!connection.Valid && connection.InTransitAtoms == 0)
                    _retiredMicroSpillScratch.Add(connection.SourceBodyId);
            }
            for (int i = 0; i < _retiredMicroSpillScratch.Count; i++)
                _domain.MicroSpills.Remove(_retiredMicroSpillScratch[i]);
        }

        private void DestroyDomain()
        {
            if (_streaming != null && _streaming.HasDeferredStep)
                _streaming.CompleteDeferredStepBlocking();
            if (_domain != null && _domain.WaterBodyPublisher != null)
                _domain.WaterBodyPublisher.Published -= OnWaterBodyRegistryPublished;
            if (_subscribedCodyRuntime != null)
            _subscribedCodyRuntime.CodyCatchmentPublished -= OnCodyCatchmentPublished;
            if (_subscribedCodyRuntime != null)
                _subscribedCodyRuntime.CompleteInitialWorldDomainPublished -= OnCompleteInitialWorldDomainPublished;
            _subscribedCodyRuntime = null;
            _domain?.Shutdown();
            _streaming = null;
            _domain = null;
            if (_domainObject != null) Destroy(_domainObject);
            _domainObject = null;
            _hasLatestPlayerWaterSample = false;
            _observedGeometryGeneration = -1;
            _appliedGeometryStateRevision = int.MinValue;
            _appliedGeometryRevisions.Clear();
            _initialGeometryPreparation = null;
            _preparedGeometryGeneration = -1;
            _preparedGeometryStateRevision = int.MinValue;
            _preparedGeometryIsInitial = false;
            _preparedGeometryRevisions.Clear();
            _deferredFill.Clear();
            _microVolumeConsolidation = null;
            _microVolumeGeometryRevision = long.MinValue;
            _completedSimulationSteps = 0;
            _deferredMvcRefreshStep = -1;
            _pendingMvcBodies.Clear();
            _deferredMvcBodies.Clear();
            _microSpillScratch.Clear();
            _retiredMicroSpillScratch.Clear();
            _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        }

        internal VolumetricStreamingDomainController Streaming => _streaming;

        private static float ParsePositive(Terminal.ConsoleEventArgs args, int index, float fallback)
        {
            return Mathf.Clamp(Parse(args, index, fallback), 0.75f, 18f);
        }

        private static float Parse(Terminal.ConsoleEventArgs args, int index, float fallback)
        {
            if (args == null || args.Length <= index) return fallback;
            float value;
            return float.TryParse(args[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static float Snap(float value, float spacing) => Mathf.Round(value / spacing) * spacing;
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "terrain";
            char[] invalid = Path.GetInvalidFileNameChars();
            var result = value.Trim();
            for (int i = 0; i < invalid.Length; i++) result = result.Replace(invalid[i], '_');
            return string.IsNullOrWhiteSpace(result) ? "terrain" : result;
        }
        private static string Format(Vector3 value) => "(" + value.x.ToString("F2") + "," + value.y.ToString("F2") + "," + value.z.ToString("F2") + ")";
        private static void Reply(Terminal.ConsoleEventArgs args, string message)
        {
            if (args != null && args.Context != null) args.Context.AddString(message);
        }
    }
}
