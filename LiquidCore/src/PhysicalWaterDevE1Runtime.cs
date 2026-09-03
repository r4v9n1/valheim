using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class PhysicalWaterDevE1Runtime : MonoBehaviour
    {
        internal static PhysicalWaterDevE1Runtime Instance { get; private set; }

        private readonly List<GameObject> _geometryRoots = new List<GameObject>();
        private readonly List<GameObject> _activeGeometryRoots = new List<GameObject>();
        private readonly List<GameObject> _changedGeometryRoots = new List<GameObject>();
        private readonly Dictionary<int, int> _coverageGeometryRevisions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _appliedGeometryRevisions = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _preparedGeometryRevisions = new Dictionary<int, int>();
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
        private long _observedGeometryGeneration = -1;
        private int _appliedGeometryStateRevision = int.MinValue;
        private float _nextCausalGeometryApplyTime;
        private Vector3 _coverageWindowOrigin = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        private Bounds _geometryCoverageBounds;
        private VolumetricSolidGeometrySdf.RebasePreparation _initialGeometryPreparation;
        private long _preparedGeometryGeneration = -1;
        private int _preparedGeometryStateRevision = int.MinValue;

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
                "Vanilla-water suppression OFF. Swimming/buoyancy/ships/fish OFF. Explicit finite fluid only.");
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

            SynchronizeGeometryIfReady();

            float fixedDt = 1f / 30f;
            _accumulator = Mathf.Min(_accumulator + Time.deltaTime, fixedDt * 3f);
            int maxSubsteps = Mathf.Clamp(PhysicalWaterPlugin.Settings.StageE1MaxSubstepsPerFrame.Value, 1, 4);
            int substeps = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!_domain.Paused && _accumulator >= fixedDt && substeps < maxSubsteps)
            {
                _streaming.Step(fixedDt);
                _accumulator -= fixedDt;
                substeps++;
            }
            watch.Stop();
            _lastSubsteps = substeps;
            _lastSimulationCpuMs = (float)watch.Elapsed.TotalMilliseconds;

            if (Time.unscaledTime >= _nextTelemetryTime)
            {
                _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
                LogTelemetry();
            }

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
                new VolumetricFiniteDomainSettings(),
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
            _accumulator = 0f;
            // Do not issue a blocking full-field diagnostic readback in the
            // same frame as F6 resource creation. The newly-created domain is
            // deliberately paused and empty; its first full telemetry sample
            // can wait for the normal interval.
            _nextTelemetryTime = Time.unscaledTime + Mathf.Max(0.5f, PhysicalWaterPlugin.Settings.StageE1TelemetryInterval.Value);
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
                Reply(args, "E1 geometry coverage is still preparing; no fluid was spawned.");
                if (args == null && Player.m_localPlayer != null)
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "PhysicalWater E1 geometry is still preparing.");
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
                if (fractions.Length != mac.CellCount || interfaceTops.Length != mac.CellCount || solidSdf.Length != mac.CellCount ||
                    columnHeights.Length != surface.SurfaceResolutionX * surface.SurfaceResolutionZ)
                {
                    PhysicalWaterPlugin.Log.LogError(
                        "PW_E3_PROBE failed readback sizes: fractions=" + fractions.Length +
                        ", interfaceTops=" + interfaceTops.Length +
                        ", sdf=" + solidSdf.Length + ", columns=" + columnHeights.Length + ".");
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
                    "reason,simSeconds,surfaceX,surfaceZ,worldX,worldZ,terrainWorldY,solidSurfaceWorldY,legacyFractionTopWorldY,interfaceTopWorldY,columnHeightWorldY,renderPreviousWorldY,renderCurrentWorldY,renderInterpolatedWorldY,topLiquidCell,topFraction,interfaceMinusTerrain,columnMinusInterface,renderMinusColumn"
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
                    float columnLocalY = columnHeights[x + sx * z];
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
                        ProbeFmt(legacyRawWorldY) + "," + ProbeFmt(rawWorldY) + "," + ProbeFmt(columnWorldY) + "," +
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
                        "PhysicalWater devE3 discarded stale initial geometry preparation generation=" + _preparedGeometryGeneration +
                        ", currentGeneration=" + currentGeneration + ".");
                    _initialGeometryPreparation = null;
                    _preparedGeometryGeneration = -1;
                    _preparedGeometryStateRevision = int.MinValue;
                    _observedGeometryGeneration = -1;
                    return;
                }
                if (_initialGeometryPreparation.IsFaulted || _initialGeometryPreparation.IsCanceled)
                {
                    PhysicalWaterPlugin.Log.LogError("PhysicalWater devE3 initial geometry preparation failed before application.");
                    _initialGeometryPreparation = null;
                    _observedGeometryGeneration = -1;
                    return;
                }

                var applyWatch = System.Diagnostics.Stopwatch.StartNew();
                if (!_streaming.TryApplyPreparedGeometry(_preparedGeometryGeneration, _initialGeometryPreparation, out VolumetricFiniteSolidUpdateDiagnostics preparedUpdate)) return;
                applyWatch.Stop();
                _appliedGeometryStateRevision = _preparedGeometryStateRevision;
                _appliedGeometryRevisions.Clear();
                foreach (KeyValuePair<int, int> pair in _preparedGeometryRevisions)
                    _appliedGeometryRevisions.Add(pair.Key, pair.Value);
                _initialGeometryPreparation = null;
                _preparedGeometryGeneration = -1;
                _preparedGeometryStateRevision = int.MinValue;
                _preparedGeometryRevisions.Clear();
                _domain.Paused = false;
                adapter.ReleaseCausalGeometryCoverage();
                _nextCausalGeometryApplyTime = Time.realtimeSinceStartup + 0.75f;
                PhysicalWaterPlugin.Log.LogInfo(
                    "PW_E3_F6_READY geometryReady=True, fillReady=True, simulationPaused=False; prepared causal solid synchronization apply=" +
                    applyWatch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + "ms: " + preparedUpdate + ".");
                if (preparedUpdate.ParticlesStillInSolid != 0 || preparedUpdate.ParticlesDeleted != 0)
                {
                    _domain.Paused = true;
                    PhysicalWaterPlugin.Log.LogError("PhysicalWater devE3 paused after unsafe prepared solid update: " + preparedUpdate + ". No vanilla-water fallback was used.");
                }
                return;
            }
            // World discovery and Valheim destruction callbacks often arrive
            // as a burst. Applying every intermediate generation would run a
            // synchronous occupancy/SDF/upload rebuild for each event and
            // turn normal loading or construction into multi-hundred-ms
            // stalls. The causal snapshot below is still the newest complete
            // state; defer only until the burst quiets so one exact rebuild
            // represents the accumulated changes.
            float now = Time.realtimeSinceStartup;
            float scanInterval = PhysicalWaterPlugin.Settings != null
                ? Mathf.Max(0.25f, PhysicalWaterPlugin.Settings.ValheimGeometryScanInterval.Value)
                : 0.25f;
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
            Bounds dirtyWorldBounds;
            Bounds? localizedDirtyWorldBounds = adapter.TryGetReadyDirtyWorldBounds(generation, out dirtyWorldBounds)
                ? dirtyWorldBounds
                : (Bounds?)null;
            // The inactive solid bank may still be retiring the preceding
            // geometry generation. Defer without consuming PCE state; the
            // same causal snapshot is retried on a later frame once its CPU
            // synchronization fence reports safe reuse.
            if (_appliedGeometryStateRevision != int.MinValue && !_domain.MacDomain.CanApplyPreparedSolidFields) return;
            pce.ApplyMappedColliderOccupancy(_domain.WorldBounds, _domain.WorldOrigin, _domain.MacDomain.Settings.CellSize);
            if (_appliedGeometryStateRevision == int.MinValue)
            {
                _preparedGeometryRevisions.Clear();
                foreach (KeyValuePair<int, int> pair in _coverageGeometryRevisions)
                    _preparedGeometryRevisions.Add(pair.Key, pair.Value);
                _preparedGeometryGeneration = generation;
                _preparedGeometryStateRevision = stateRevision;
                _initialGeometryPreparation = _streaming.BeginPrepareGeometry(generation, _geometryRoots);
                PhysicalWaterPlugin.Log.LogInfo(
                    "PhysicalWater devE3 began background initial geometry preparation generation=" + generation +
                    ", roots=" + _geometryRoots.Count + ". Fill remains gated until atomic application.");
                return;
            }
            VolumetricFiniteSolidUpdateDiagnostics update = _streaming.SynchronizeGeometry(
                generation,
                _geometryRoots,
                _changedGeometryRoots,
                localizedDirtyWorldBounds,
                synchronizeDiagnostics: false);
            _appliedGeometryStateRevision = stateRevision;
            _appliedGeometryRevisions.Clear();
            foreach (KeyValuePair<int, int> pair in _coverageGeometryRevisions)
                _appliedGeometryRevisions.Add(pair.Key, pair.Value);
            _domain.Paused = false;
            adapter.ReleaseCausalGeometryCoverage();
            _nextCausalGeometryApplyTime = now + Mathf.Max(0.75f, scanInterval * 3f);
            PhysicalWaterPlugin.Log.LogInfo(
                "PW_E3_F6_READY geometryReady=True, fillReady=True, simulationPaused=False; causal solid synchronization: " + update + ".");
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
            PhysicalWaterPlugin.Log.LogInfo("PhysicalWater devE3 requested causal geometry coverage for current window plus one 24m logical-region margin: " + _geometryCoverageBounds + ".");
        }

        private void LogTelemetry()
        {
            VolumetricStreamingDiagnostics streaming = _streaming.Diagnostics;
            VolumetricWaterDomain mac = _domain.MacDomain;
            PhysicalWaterPlugin.Log.LogInfo(
                "PhysicalWater devE3 nonblocking telemetry: paused=" + _domain.Paused +
                ", particles=" + _domain.ParticleCount +
                ", substeps=" + _lastSubsteps +
                ", activeCpuMs=" + _lastSimulationCpuMs.ToString("F3") +
                ", simulatedSeconds=" + (mac != null ? mac.Diagnostics.SimulatedSeconds.ToString("F3") : "n/a") +
                ", flipTiming=(" + _domain.FlipDomain.LastStepTimings + ")" +
                ", macTiming=(" + (mac != null ? mac.LastStepTimings.ToString() : "n/a") + ")" +
                ", streaming=(" + streaming + ")" +
                ", globalOcean=False, vanillaFallback=False, hiddenReseeding=False.");
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
            return true;
        }

        private void DestroyDomain()
        {
            _streaming = null;
            _domain = null;
            if (_domainObject != null) Destroy(_domainObject);
            _domainObject = null;
            _observedGeometryGeneration = -1;
            _appliedGeometryStateRevision = int.MinValue;
            _appliedGeometryRevisions.Clear();
            _initialGeometryPreparation = null;
            _preparedGeometryGeneration = -1;
            _preparedGeometryStateRevision = int.MinValue;
            _preparedGeometryRevisions.Clear();
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
