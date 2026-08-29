using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

[assembly: AssemblyTitle(Terramizer.TerramizerPlugin.PluginName)]
[assembly: AssemblyVersion(Terramizer.TerramizerPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(Terramizer.TerramizerPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(Terramizer.TerramizerPlugin.CreatorCredit)]
[assembly: AssemblyProduct(Terramizer.TerramizerPlugin.PluginName)]
[assembly: AssemblyCopyright(Terramizer.TerramizerPlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", Terramizer.TerramizerPlugin.CreatorCredit)]

namespace Terramizer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class TerramizerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.terramizer";
        public const string PluginName = "Terramizer";
        public const string PluginVersion = "0.9.5";
        public const string CreatorCredit = "Created by R4V9N1";
        private const string ServerVersionKey = "r4v9n1.terramizerserver.version";
        private const string ServerStaticOwnershipKey = "r4v9n1.terramizerserver.staticOwnership";
        private const string RpcRequestCompanionMetadata = "r4v9n1.terramizerserver.RequestCompanionMetadata";
        private const string RpcCompanionMetadata = "r4v9n1.terramizerserver.CompanionMetadata";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _enableServerCompanionOptimizations;
        private static ConfigEntry<bool> _autoDetectTerramizerServer;
        private static ConfigEntry<bool> _forceServerCompanionMode;
        private static ConfigEntry<bool> _logServerCompanionDetection;
        private static ConfigEntry<float> _serverCompanionWearNTearMinIntervalSeconds;
        private static ConfigEntry<float> _serverCompanionSmokeMinIntervalSeconds;
        private static ConfigEntry<bool> _coalesceClutterResets;
        private static ConfigEntry<float> _clutterResetCoalesceSeconds;
        private static ConfigEntry<bool> _deferGrassRebuildAfterTerrain;
        private static ConfigEntry<float> _grassRebuildTerrainSettleSeconds;
        private static ConfigEntry<bool> _paceWearNTearUpdates;
        private static ConfigEntry<float> _wearNTearMinIntervalSeconds;
        private static ConfigEntry<bool> _paceSmokeUpdates;
        private static ConfigEntry<float> _smokeMinIntervalSeconds;
        private static ConfigEntry<bool> _disableUnityJobDebugger;
        private static ConfigEntry<bool> _performanceLoggingEnabled;
        private static ConfigEntry<float> _reportIntervalSeconds;

        private static bool _hasPendingClutterReset;
        private static bool _flushingClutterReset;
        private static Vector3 _pendingClutterCenter;
        private static float _pendingClutterRadius;
        private static float _nextClutterResetFlushTime;
        private static float _lastHeightmapRegenerateTime = float.NegativeInfinity;
        private static float _nextWearNTearUpdateTime;
        private static long _coalescedGrassResetCalls;
        private static long _deferredGrassRebuilds;
        private static long _pacedWearNTearPasses;
        private static long _pacedSmokePasses;
        private static bool _serverCompanionModeActive;
        private static bool _serverCompanionModeLogged;
        private static bool _serverCompanionRpcDetected;
        private static bool _serverCompanionRpcStaticOwnership;
        private static bool _serverCompanionRpcOwnershipCache;
        private static long _serverCompanionRpcPeerUid;
        private static string _detectedServerCompanionVersion = "";
        private static ManualLogSource _log;
        private static ZRoutedRpc _registeredRoutedRpcInstance;
        private static bool _applicationQuitting;

        private Harmony _harmony;
        private float _nextReportTime;
        private float _nextServerCompanionCheckTime;
        private float _nextServerCompanionDiagnosticTime;
        private float _nextServerCompanionRpcRequestTime;
        private float _smoothedFrameSeconds;

        private void Awake()
        {
            _log = Logger;
            BindConfig();
            RemoveRetiredSettings();

            if (Application.isBatchMode)
            {
                Logger.LogInfo(PluginName + " " + PluginVersion + " is a client mod and remains inactive in batch mode.");
                return;
            }

            _harmony = new Harmony(PluginGuid);
            PatchFeature(typeof(ClutterSystemResetGrassCoalescePatch), "grass-reset coalescing");
            PatchFeature(typeof(ClutterSystemUpdateGrassPacingPatch), "grass-rebuild settling");
            PatchFeature(typeof(HeightmapRegenerateObservationPatch), "terrain-rebuild observation");
            PatchFeature(typeof(WearNTearUpdaterPacingPatch), "client structure-update pacing");
            PatchFeature(typeof(SmokeRendererPacingPatch), "smoke-update pacing");
            ApplySafeCpuSetting();
            RegisterCompanionRpcsWhenReady();

            _nextReportTime = Time.realtimeSinceStartup + _reportIntervalSeconds.Value;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded with standalone client performance smoothing.");
            Logger.LogInfo("Active scope: grass-reset coalescing, grass-rebuild settling, client WearNTear updater pacing, smoke pacing, and optional diagnostics.");
            Logger.LogInfo("TerramizerServer companion detection is " + (_autoDetectTerramizerServer.Value ? "enabled" : "disabled") + "; manual companion mode=" + _forceServerCompanionMode.Value + ".");
            Logger.LogInfo("Terrain generation, terrain modifiers, terrain compiler lookup, world-object streaming, structural support results, networking, ownership, and saves remain vanilla.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private void BindConfig()
        {
            _enabled = Config.Bind("General", "Enabled", true,
                "Enable Terramizer's proven client-side smoothing features.");
            _enableServerCompanionOptimizations = Config.Bind("ServerCompanion", "EnableServerCompanionOptimizations", true,
                "When TerramizerServer is detected on the connected server, use slightly stronger client-side smoothing that pairs with server-owned static pieces.");
            _autoDetectTerramizerServer = Config.Bind("ServerCompanion", "AutoDetectTerramizerServer", true,
                "Detect TerramizerServer through a lightweight RPC handshake, with server-synced metadata as a fallback.");
            _forceServerCompanionMode = Config.Bind("ServerCompanion", "ForceServerCompanionMode", false,
                "Force Terramizer's server-companion smoothing profile even when metadata is not detected. Useful while testing older TerramizerServer builds.");
            _logServerCompanionDetection = Config.Bind("ServerCompanion", "LogServerCompanionDetection", true,
                "Log compact detection status while waiting for TerramizerServer metadata. Useful during compatibility testing.");
            _serverCompanionWearNTearMinIntervalSeconds = BindRange("ServerCompanion", "ServerCompanionWearNTearMinIntervalSeconds", 0.16f,
                "Minimum interval between client WearNTear updater passes when TerramizerServer companion mode is active.", 0.02f, 0.75f);
            _serverCompanionSmokeMinIntervalSeconds = BindRange("ServerCompanion", "ServerCompanionSmokeMinIntervalSeconds", 0.005f,
                "Minimum interval between accepted smoke-renderer update passes when TerramizerServer companion mode is active. Lower values keep smoke visually smoother.", 0.005f, 0.5f);
            _coalesceClutterResets = Config.Bind("Clutter", "CoalesceGrassResetBursts", true,
                "Combine repeated grass-reset requests into one short delayed reset without lowering grass density or distance.");
            _clutterResetCoalesceSeconds = BindRange("Clutter", "GrassResetCoalesceSeconds", 0.25f,
                "Seconds used to collect a burst of nearby grass-reset requests.", 0.05f, 2f);
            _deferGrassRebuildAfterTerrain = Config.Bind("Clutter", "DeferGrassRebuildAfterTerrain", true,
                "Let terrain regeneration settle briefly before a full grass rebuild.");
            _grassRebuildTerrainSettleSeconds = BindRange("Clutter", "GrassRebuildTerrainSettleSeconds", 0.35f,
                "Seconds a full grass rebuild may wait after terrain regeneration.", 0.05f, 2f);
            _paceWearNTearUpdates = Config.Bind("Structures", "PaceWearNTearUpdates", true,
                "Pace the client WearNTear updater in build-heavy areas. This does not alter support calculations, durability, damage, or saved pieces.");
            _wearNTearMinIntervalSeconds = BindRange("Structures", "WearNTearMinIntervalSeconds", 0.08f,
                "Minimum interval between client WearNTear updater passes.", 0.01f, 0.5f);
            _paceSmokeUpdates = Config.Bind("Effects", "PaceSmokeUpdates", true,
                "Pace client smoke-renderer updates to reduce fire and smelter overhead.");
            _smokeMinIntervalSeconds = BindRange("Effects", "SmokeMinIntervalSeconds", 0.005f,
                "Minimum interval between accepted smoke-renderer update passes. Lower values keep smoke visually smoother.", 0.005f, 0.25f);
            _disableUnityJobDebugger = Config.Bind("Performance", "DisableUnityJobDebugger", true,
                "Disable Unity's development-only job debugger without changing worker counts.");
            _performanceLoggingEnabled = Config.Bind("Diagnostics", "PerformanceLoggingEnabled", false,
                "Log compact FPS and smoothing counters for troubleshooting.");
            _reportIntervalSeconds = BindRange("Diagnostics", "ReportIntervalSeconds", 30f,
                "Seconds between diagnostic summaries.", 15f, 300f);
        }

        private ConfigEntry<T> BindRange<T>(string section, string key, T defaultValue, string description, T minimum, T maximum)
            where T : IComparable
        {
            return Config.Bind(section, key, defaultValue,
                new ConfigDescription(description, new AcceptableValueRange<T>(minimum, maximum)));
        }

        private void PatchFeature(Type patchType, string featureName)
        {
            try
            {
                _harmony.CreateClassProcessor(patchType).Patch();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not install " + featureName + "; that feature will use vanilla behavior: " + ex.Message);
            }
        }

        private void Update()
        {
            RegisterCompanionRpcsWhenReady();
            RefreshServerCompanionMode();
            FlushPendingClutterResetIfDue(false);

            if (!_performanceLoggingEnabled.Value || !Application.isFocused)
            {
                return;
            }

            float frameSeconds = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.25f);
            _smoothedFrameSeconds = _smoothedFrameSeconds <= 0f
                ? frameSeconds
                : Mathf.Lerp(_smoothedFrameSeconds, frameSeconds, 0.025f);

            float now = Time.realtimeSinceStartup;
            if (now < _nextReportTime)
            {
                return;
            }

            _nextReportTime = now + _reportIntervalSeconds.Value;
            float fps = _smoothedFrameSeconds > 0f ? 1f / _smoothedFrameSeconds : 0f;
            Logger.LogInfo("Performance: " + fps.ToString("F1") + " FPS; coalesced grass resets=" + TakeCounter(ref _coalescedGrassResetCalls) +
                ", deferred grass rebuilds=" + TakeCounter(ref _deferredGrassRebuilds) +
                ", paced structure passes=" + TakeCounter(ref _pacedWearNTearPasses) +
                ", paced smoke passes=" + TakeCounter(ref _pacedSmokePasses) +
                ", server companion mode=" + _serverCompanionModeActive +
                (_detectedServerCompanionVersion.Length > 0 ? " (" + _detectedServerCompanionVersion + ")" : "") + ".");
        }

        private void OnDestroy()
        {
            if (_harmony != null && !_applicationQuitting)
            {
                try
                {
                    _harmony.UnpatchSelf();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Could not unpatch Terramizer during teardown; process shutdown can continue safely: " + ex.Message);
                }
                _harmony = null;
            }

            FlushPendingClutterResetIfDue(true);
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void ApplySafeCpuSetting()
        {
            if (!_enabled.Value || !_disableUnityJobDebugger.Value)
            {
                return;
            }

            try
            {
                if (JobsUtility.JobDebuggerEnabled)
                {
                    JobsUtility.JobDebuggerEnabled = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not change Unity's optional job-debugger setting: " + ex.Message);
            }
        }

        internal static bool QueueClutterReset(Vector3 center, float radius)
        {
            if (!IsEnabled() || _flushingClutterReset || !_coalesceClutterResets.Value)
            {
                return true;
            }

            float requestedRadius = Math.Max(0f, radius);
            if (!_hasPendingClutterReset)
            {
                _pendingClutterCenter = center;
                _pendingClutterRadius = requestedRadius;
                _hasPendingClutterReset = true;
            }
            else
            {
                Vector3 offset = center - _pendingClutterCenter;
                offset.y = 0f;
                float distance = offset.magnitude;
                if (distance + requestedRadius > _pendingClutterRadius)
                {
                    float mergedRadius = (_pendingClutterRadius + distance + requestedRadius) * 0.5f;
                    if (distance > 0.001f)
                    {
                        _pendingClutterCenter += offset.normalized * (mergedRadius - _pendingClutterRadius);
                    }
                    _pendingClutterRadius = mergedRadius;
                }
            }

            _nextClutterResetFlushTime = Time.realtimeSinceStartup + _clutterResetCoalesceSeconds.Value;
            _coalescedGrassResetCalls++;
            return false;
        }

        internal static void MarkHeightmapRegenerated()
        {
            _lastHeightmapRegenerateTime = Time.realtimeSinceStartup;
        }

        internal static bool ShouldRunClutterUpdate(bool rebuildAll)
        {
            if (!IsEnabled() || !rebuildAll || !_deferGrassRebuildAfterTerrain.Value)
            {
                return true;
            }

            if (Time.realtimeSinceStartup - _lastHeightmapRegenerateTime >= _grassRebuildTerrainSettleSeconds.Value)
            {
                return true;
            }

            _deferredGrassRebuilds++;
            return false;
        }

        internal static bool ShouldRunWearNTearUpdate()
        {
            if (!IsEnabled() || !_paceWearNTearUpdates.Value)
            {
                return true;
            }

            float now = Time.realtimeSinceStartup;
            float interval = GetWearNTearInterval();
            if (now >= _nextWearNTearUpdateTime)
            {
                _nextWearNTearUpdateTime = now + interval;
                return true;
            }

            _pacedWearNTearPasses++;
            return false;
        }

        internal static bool ShouldRunSmokeUpdate(SmokeRenderer renderer)
        {
            if (!IsEnabled() || !_paceSmokeUpdates.Value)
            {
                return true;
            }

            float now = Time.realtimeSinceStartup;
            float interval = GetSmokeInterval();

            float frameSeconds = Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.1f);
            if (interval <= frameSeconds * 1.1f)
            {
                return true;
            }

            float phase = GetStableSmokePhase(renderer, interval);
            float previousBucket = Mathf.Floor((now - frameSeconds + phase) / interval);
            float currentBucket = Mathf.Floor((now + phase) / interval);
            if (currentBucket > previousBucket)
            {
                return true;
            }

            _pacedSmokePasses++;
            return false;
        }

        private static float GetStableSmokePhase(SmokeRenderer renderer, float interval)
        {
            int id = renderer != null ? renderer.GetInstanceID() : 0;
            unchecked
            {
                uint hash = (uint)id;
                hash ^= hash >> 16;
                hash *= 0x7feb352dU;
                hash ^= hash >> 15;
                hash *= 0x846ca68bU;
                hash ^= hash >> 16;
                return (hash & 0xffffU) / 65535f * interval;
            }
        }

        private static float GetWearNTearInterval()
        {
            if (IsServerCompanionModeActive())
            {
                return Mathf.Max(_wearNTearMinIntervalSeconds.Value, _serverCompanionWearNTearMinIntervalSeconds.Value);
            }

            return _wearNTearMinIntervalSeconds.Value;
        }

        private static float GetSmokeInterval()
        {
            if (IsServerCompanionModeActive())
            {
                return Mathf.Max(_smokeMinIntervalSeconds.Value, _serverCompanionSmokeMinIntervalSeconds.Value);
            }

            return _smokeMinIntervalSeconds.Value;
        }

        private void FlushPendingClutterResetIfDue(bool force)
        {
            if (!_hasPendingClutterReset || (!force && IsEnabled() && Time.realtimeSinceStartup < _nextClutterResetFlushTime))
            {
                return;
            }

            ClutterSystem clutter = ClutterSystem.instance;
            if (clutter == null)
            {
                _hasPendingClutterReset = false;
                return;
            }

            _flushingClutterReset = true;
            try
            {
                clutter.ResetGrass(_pendingClutterCenter, _pendingClutterRadius);
            }
            finally
            {
                _flushingClutterReset = false;
                _hasPendingClutterReset = false;
            }
        }

        private static bool IsEnabled()
        {
            return _enabled != null && _enabled.Value;
        }

        private static bool IsServerCompanionModeActive()
        {
            return _enableServerCompanionOptimizations != null && _enableServerCompanionOptimizations.Value && _serverCompanionModeActive;
        }

        private void RefreshServerCompanionMode()
        {
            if (_enableServerCompanionOptimizations == null || !_enableServerCompanionOptimizations.Value)
            {
                _serverCompanionModeActive = false;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextServerCompanionCheckTime)
            {
                return;
            }
            _nextServerCompanionCheckTime = now + 2f;

            bool active = _forceServerCompanionMode.Value;
            string version = "";
            string staticOwnership = "";
            int syncedKeyCount = -1;
            bool hasServerPeer = false;
            bool rpcDetected = _serverCompanionRpcDetected;

            if (!active && _autoDetectTerramizerServer.Value && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
                if (serverPeer != null)
                {
                    hasServerPeer = true;
                    if (_serverCompanionRpcDetected && _serverCompanionRpcPeerUid != 0L && _serverCompanionRpcPeerUid != serverPeer.m_uid)
                    {
                        ClearCompanionRpcDetection();
                    }
                    RequestCompanionMetadataIfDue(serverPeer, now);

                    if (_serverCompanionRpcDetected)
                    {
                        version = _detectedServerCompanionVersion;
                        staticOwnership = _serverCompanionRpcStaticOwnership.ToString();
                        active = _serverCompanionRpcStaticOwnership;
                    }
                    else if (serverPeer.m_serverSyncedPlayerData != null)
                    {
                        syncedKeyCount = serverPeer.m_serverSyncedPlayerData.Count;
                        bool hasVersion = serverPeer.m_serverSyncedPlayerData.TryGetValue(ServerVersionKey, out version);
                        bool hasStaticOwnership = serverPeer.m_serverSyncedPlayerData.TryGetValue(ServerStaticOwnershipKey, out staticOwnership);
                        if (hasVersion)
                        {
                            active = !hasStaticOwnership || string.Equals(staticOwnership, "True", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            _serverCompanionModeActive = active;
            _detectedServerCompanionVersion = version ?? "";
            if (_serverCompanionModeActive && !_serverCompanionModeLogged)
            {
                _serverCompanionModeLogged = true;
                Logger.LogInfo("TerramizerServer companion mode active" +
                               (_detectedServerCompanionVersion.Length > 0 ? " from server version " + _detectedServerCompanionVersion : " by manual override") +
                               (_serverCompanionRpcDetected ? " via RPC" : "") +
                               ". Client WearNTear interval=" + GetWearNTearInterval().ToString("F2") +
                               "s, smoke interval=" + GetSmokeInterval().ToString("F2") + "s.");
            }
            else if (!_serverCompanionModeActive && _logServerCompanionDetection.Value && now >= _nextServerCompanionDiagnosticTime && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                _nextServerCompanionDiagnosticTime = now + 30f;
                Logger.LogInfo("TerramizerServer companion detection waiting: serverPeer=" + hasServerPeer +
                               ", rpcDetected=" + rpcDetected +
                               ", syncedKeys=" + syncedKeyCount +
                               ", version='" + (version ?? "") +
                               "', staticOwnership='" + (staticOwnership ?? "") + "'.");
            }
        }

        private void RegisterCompanionRpcsWhenReady()
        {
            if (ZRoutedRpc.instance == null || object.ReferenceEquals(_registeredRoutedRpcInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register<string, bool, bool>(RpcCompanionMetadata, OnCompanionMetadata);
            _registeredRoutedRpcInstance = ZRoutedRpc.instance;
        }

        private void RequestCompanionMetadataIfDue(ZNetPeer serverPeer, float now)
        {
            if (serverPeer == null || ZRoutedRpc.instance == null || now < _nextServerCompanionRpcRequestTime)
            {
                return;
            }

            _nextServerCompanionRpcRequestTime = now + 10f;
            try
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer.m_uid, RpcRequestCompanionMetadata);
            }
            catch (Exception ex)
            {
                if (_logServerCompanionDetection != null && _logServerCompanionDetection.Value && now >= _nextServerCompanionDiagnosticTime)
                {
                    Logger.LogInfo("TerramizerServer companion RPC request is waiting for routing readiness: " + ex.Message);
                }
            }
        }

        private static void OnCompanionMetadata(long sender, string version, bool staticOwnership, bool ownershipCache)
        {
            _serverCompanionRpcDetected = !string.IsNullOrEmpty(version);
            _serverCompanionRpcStaticOwnership = staticOwnership;
            _serverCompanionRpcOwnershipCache = ownershipCache;
            _serverCompanionRpcPeerUid = sender;
            _detectedServerCompanionVersion = version ?? "";
        }

        private static void ClearCompanionRpcDetection()
        {
            _serverCompanionRpcDetected = false;
            _serverCompanionRpcStaticOwnership = false;
            _serverCompanionRpcOwnershipCache = false;
            _serverCompanionRpcPeerUid = 0L;
            _detectedServerCompanionVersion = "";
            _serverCompanionModeLogged = false;
        }

        private static long TakeCounter(ref long counter)
        {
            long value = counter;
            counter = 0;
            return value;
        }

        private void RemoveRetiredSettings()
        {
            Remove("General", "ApplySafeDefaultsOnce", true);
            Remove("General", "CompatibilityDefaultsVersion", 0);
            Remove("Performance", "ReuseHeightmapModifierBuffers", true);
            Remove("Performance", "CacheTerrainCompilerLookupsV2", true);
            Remove("Performance", "DisableUnityJobDebuggerV2", true);
            Remove("Performance", "EnableCompatibleMaterialInstancing", true);
            Remove("Performance", "RescanStreamedMaterials", false);
            Remove("Performance", "MaterialScanIntervalSeconds", 120f);
            Remove("Performance", "RenderersProcessedPerFrame", 192);
            Remove("AdaptiveLighting", "Enabled", true);
            Remove("AdaptiveLighting", "TargetFPS", 60);
            Remove("AdaptiveLighting", "BalancedPointLights", 40);
            Remove("AdaptiveLighting", "BalancedPointLightShadows", 3);
            Remove("AdaptiveLighting", "ConstrainedPointLights", 15);
            Remove("AdaptiveLighting", "ConstrainedPointLightShadows", 1);
            Remove("AdaptiveLighting", "CriticalPointLights", 8);
            Remove("AdaptiveLighting", "CriticalPointLightShadows", 0);
            Remove("AdaptiveLighting", "AdaptDirectionalShadows", true);
            Remove("AdaptiveLighting", "EvaluationIntervalSeconds", 5f);
            Remove("AdaptiveLighting", "TierChangeCooldownSeconds", 15f);
            Remove("AdaptiveRendering", "AdaptGeometryLod", false);
            Remove("AdaptiveRendering", "ConstrainedLodBias", 3.5f);
            Remove("AdaptiveRendering", "CriticalLodBias", 2.75f);
            Remove("Quality", "EnhanceTextureClarity", true);
            Remove("Quality", "MinimumLodBias", 2f);
            Remove("Quality", "TextureStreamingBudgetMB", 1536);
            Remove("Quality", "TextureStreamingRenderersPerFrame", 512);
            Remove("Profiler", "PerformanceLoggingEnabled", false);
            Remove("Profiler", "ReportIntervalSeconds", 15f);
            Remove("Profiler", "Enabled", false);
            Remove("Terrain", "DistantTerrainModifierCullDistance", 0f);
            Remove("Terrain", "DistantTerrainCompCullDistance", 0f);
            Remove("Terrain", "SkipDistantTerrainRebuildPokesV2", false);
            Remove("Terrain", "MaxHeightmapRegenerationsPerFrame", 0);
            Remove("Terrain", "OptimizeHeightmapApplyModifiers", false);
            Remove("Terrain", "OptimizeCloseTerrainCompApply", false);
            Remove("Terrain", "CacheTerrainCompilerLookups", false);
            Remove("Memory", "PeriodicUnloadUnusedAssets", false);
            Remove("Memory", "UnusedAssetCleanupIntervalSeconds", 600f);
            Remove("Memory", "ForceGarbageCollectAfterUnusedAssetCleanup", false);
            Remove("CPU", "UnityJobWorkerCountV2", -1);
            Remove("CPU", "DisableUnityJobDebugger", false);
            Remove("Disk", "DeleteOldBepInExLogsOnStart", false);
            Remove("Disk", "DeleteLogsOlderThanDays", 14);
            Remove("Debug", "LogSkippedTerrainWork", false);
            Remove("WorldObjects", "ZNetSceneProcessIntervalSeconds", 0.033f);
            Remove("WorldObjects", "ZNetSceneBusyAreaIntervalMultiplier", 1f);
            Remove("WorldObjects", "ZNetSceneSpikeThresholdMs", 6f);
            Remove("WorldObjects", "ZNetSceneBusyAreaHoldSeconds", 0f);
            Remove("WorldObjects", "ZNetSceneSpawnSmoothingSeconds", 0f);
            Remove("WorldObjects", "TimeSliceCreateObjects", false);
            Remove("WorldObjects", "CreateObjectsBudgetMs", 3f);
            Remove("WorldObjects", "CreateObjectsMaxInstancesPerFrame", 10);
            Remove("WorldObjects", "CreateObjectsSafetyFallbackEnabled", false);
            Remove("WorldObjects", "CreateObjectsSafetyFallbackThreshold", 5000);
            Config.Save();
        }

        private void Remove<T>(string section, string key, T defaultValue)
        {
            ConfigEntry<T> retired = Config.Bind(section, key, defaultValue, "Retired setting.");
            Config.Remove(retired.Definition);
        }
    }
}
