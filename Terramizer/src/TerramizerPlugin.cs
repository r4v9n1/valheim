using System;
using System.Globalization;
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
    public sealed partial class TerramizerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.terramizer";
        public const string PluginName = "Terramizer";
        public const string PluginVersion = "1.0.3";
        public const string CreatorCredit = "Created by R4V9N1";
        private const string ServerVersionKey = "r4v9n1.terramizerserver.version";
        private const string ServerStaticOwnershipKey = "r4v9n1.terramizerserver.staticOwnership";
        private const string ServerTerrainLimitsEnabledKey = "r4v9n1.terramizerserver.extendedTerrainLimits";
        private const string ServerTerrainRaiseLimitKey = "r4v9n1.terramizerserver.terrainRaiseLimit";
        private const string ServerTerrainDigLimitKey = "r4v9n1.terramizerserver.terrainDigLimit";
        private const string RpcRequestCompanionMetadata = "r4v9n1.terramizerserver.RequestCompanionMetadata";
        private const string RpcCompanionMetadata = "r4v9n1.terramizerserver.CompanionMetadata";
        private const string RpcCompanionMetadataV2 = "r4v9n1.terramizerserver.CompanionMetadataV2";
        private const string RpcRequestCompanionMetadataV2 = "r4v9n1.terramizerserver.RequestCompanionMetadataV2";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _enableServerCompanionOptimizations;
        private static ConfigEntry<bool> _autoDetectTerramizerServer;
        private static ConfigEntry<bool> _forceServerCompanionMode;
        private static ConfigEntry<bool> _logServerCompanionDetection;
        private static ConfigEntry<bool> _disableUnityJobDebugger;
        private static ConfigEntry<bool> _reuseCollisionCallbacks;
        private static ConfigEntry<bool> _performanceLoggingEnabled;
        private static ConfigEntry<float> _reportIntervalSeconds;

        private static bool _serverCompanionModeActive;
        private static bool _serverCompanionHandshakeValid;
        private static bool _serverCompanionModeLogged;
        private static bool _serverCompanionRpcDetected;
        private static bool _serverCompanionRpcV2Detected;
        private static bool _serverCompanionLegacyRequestSent;
        private static int _serverCompanionV2RequestAttempts;
        private static bool _serverCompanionRpcStaticOwnership;
        private static bool _serverCompanionRpcExtendedTerrain;
        private static float _serverCompanionRpcTerrainRaiseLimit = 8f;
        private static float _serverCompanionRpcTerrainDigLimit = 8f;
        private static bool _serverExtendedTerrain;
        private static float _serverTerrainRaiseLimit = 8f;
        private static float _serverTerrainDigLimit = 8f;
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

            if (Application.isBatchMode)
            {
                Logger.LogInfo(PluginName + " " + PluginVersion + " is a client mod and remains inactive in batch mode.");
                return;
            }

            _harmony = new Harmony(PluginGuid);
            if (_enabled.Value)
            {
                PatchFeature(typeof(PiecePlacementEffectScopePatch), "piece placement-effect limiting");
                PatchFeature(typeof(PlayerPlacePieceEffectScopePatch), "player placement transaction-effect limiting");
                PatchFeature(typeof(PlacementEffectCleanupPatch), "placement-effect smoke cleanup");
                PatchFeature(typeof(SceneInstanceSafetyPatch), "stale scene-instance sanitation");
                PatchFeature(typeof(HeightmapAtMaxWorldLevelDepthTerrainLimitPatch), "local world heightmap dig-depth terrain limit");
                PatchFeature(typeof(HeightmapLevelTerrainTerrainLimitPatch), "local heightmap raise/dig limits");
                PatchFeature(typeof(TerrainCompLevelTerrainLimitPatch), "local terrain component raise/dig limits");
                PatchFeature(typeof(TerrainCompRaiseTerrainLimitPatch), "local terrain component direct raise limit");
                PatchFeature(typeof(TerrainCompApplyToHeightmapTerrainLimitPatch), "local terrain final-apply limits");
            }
            ApplySafeCpuSetting();
            BinarySearchDictionarySetValuePatch.Install(_harmony, Logger.LogInfo);
            if (_enabled.Value)
            {
                PatchFeature(typeof(VisEquipmentIntCachePatch), "VisEquipment ZDO integer lookup cache");
                PatchFeature(typeof(ZPackageWritePackagePatch), "allocation-free ZPackage nesting");
            }
            RegisterCompanionRpcsWhenReady();

            _nextReportTime = Time.realtimeSinceStartup + _reportIntervalSeconds.Value;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded with immediate vanilla vegetation updates.");
            Logger.LogInfo("Active scope: scoped placement-effect limiting and optional diagnostics; gameplay update cadence remains vanilla.");
            Logger.LogInfo("TerramizerServer companion detection is " + (_autoDetectTerramizerServer.Value ? "enabled" : "disabled") + "; manual companion mode=" + _forceServerCompanionMode.Value + ".");
            Logger.LogInfo("Remote-server terrain authority, terrain modifiers, world-object streaming, structural support results, networking, ownership, and saves remain vanilla; local terrain limits are extended to 16 m.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private void BindConfig()
        {
            _enabled = Config.Bind("General", "Enabled", true,
                "Enable Terramizer's client-side safeguards.");
            _enableServerCompanionOptimizations = Config.Bind("ServerCompanion", "EnableServerCompanionOptimizations", true,
                "Enable lightweight TerramizerServer detection and diagnostics; client gameplay update cadence remains vanilla.");
            _autoDetectTerramizerServer = Config.Bind("ServerCompanion", "AutoDetectTerramizerServer", true,
                "Detect TerramizerServer through a lightweight RPC handshake, with server-synced metadata as a fallback.");
            _forceServerCompanionMode = Config.Bind("ServerCompanion", "ForceServerCompanionMode", false,
                "Force companion detection status for compatibility testing; no client update cadence is changed.");
            _logServerCompanionDetection = Config.Bind("ServerCompanion", "LogServerCompanionDetection", true,
                "Log compact detection status while waiting for TerramizerServer metadata. Useful during compatibility testing.");
            _disableUnityJobDebugger = Config.Bind("Performance", "DisableUnityJobDebugger", true,
                "Disable Unity's development-only job debugger without changing worker counts.");
            _reuseCollisionCallbacks = Config.Bind("Performance", "ReuseCollisionCallbacks", true,
                "Reuse Unity collision callback objects to reduce physics GC allocations. Disable for mods that retain Collision objects after callbacks.");
            _performanceLoggingEnabled = Config.Bind("Diagnostics", "PerformanceLoggingEnabled", false,
                "Log compact FPS and companion-detection counters for troubleshooting.");
            _reportIntervalSeconds = BindRange("Diagnostics", "ReportIntervalSeconds", 30f,
                "Seconds between diagnostic summaries.", 15f, 300f);
            BindTerrainCompatibilityConfig();
            RemoveRetiredSettings();
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
                Logger.LogInfo("Installed " + featureName + ".");
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
            Logger.LogInfo("Performance: " + fps.ToString("F1") + " FPS; server companion mode=" + _serverCompanionModeActive +
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

        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
        }

        private void ApplySafeCpuSetting()
        {
            if (!_enabled.Value)
            {
                return;
            }

            try
            {
                if (_disableUnityJobDebugger.Value && JobsUtility.JobDebuggerEnabled)
                {
                    JobsUtility.JobDebuggerEnabled = false;
                }
                Physics.reuseCollisionCallbacks = _reuseCollisionCallbacks.Value;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not change Unity's optional job-debugger setting: " + ex.Message);
            }
        }

        internal static bool ShouldRunWearNTearUpdate()
        {
            // Kept as a compatibility entry point for older integrations. Terramizer
            // never suppresses WearNTear ticks: support, damage, and visible updates
            // must retain vanilla cadence.
            return true;
        }

        private void RefreshServerCompanionMode()
        {
            if (_enableServerCompanionOptimizations == null || !_enableServerCompanionOptimizations.Value)
            {
                _serverCompanionModeActive = false;
                _serverCompanionHandshakeValid = false;
                _serverExtendedTerrain = false;
                _serverTerrainRaiseLimit = 8f;
                _serverTerrainDigLimit = 8f;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextServerCompanionCheckTime)
            {
                return;
            }
            _nextServerCompanionCheckTime = now + 2f;

            if (_autoDetectTerramizerServer == null || !_autoDetectTerramizerServer.Value)
            {
                _serverCompanionHandshakeValid = false;
                _serverExtendedTerrain = false;
                _serverTerrainRaiseLimit = 8f;
                _serverTerrainDigLimit = 8f;
            }

            bool active = _forceServerCompanionMode.Value;
            string version = "";
            string staticOwnership = "";
            int syncedKeyCount = -1;
            bool hasServerPeer = false;
            bool rpcDetected = _serverCompanionRpcDetected;
            bool handshakeValid = false;
            bool extendedTerrain = false;
            float terrainRaiseLimit = 8f;
            float terrainDigLimit = 8f;

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

                    if (_serverCompanionRpcV2Detected)
                    {
                        handshakeValid = true;
                        version = _detectedServerCompanionVersion;
                        staticOwnership = _serverCompanionRpcStaticOwnership.ToString();
                        active = _serverCompanionRpcStaticOwnership;
                        extendedTerrain = _serverCompanionRpcExtendedTerrain;
                        terrainRaiseLimit = NormalizeRemoteTerrainLimit(_serverCompanionRpcTerrainRaiseLimit, extendedTerrain);
                        terrainDigLimit = NormalizeRemoteTerrainLimit(_serverCompanionRpcTerrainDigLimit, extendedTerrain);
                    }
                    else if (serverPeer.m_serverSyncedPlayerData != null &&
                             serverPeer.m_serverSyncedPlayerData.ContainsKey(ServerVersionKey))
                    {
                        syncedKeyCount = serverPeer.m_serverSyncedPlayerData.Count;
                        bool hasVersion = serverPeer.m_serverSyncedPlayerData.TryGetValue(ServerVersionKey, out version);
                        bool hasStaticOwnership = serverPeer.m_serverSyncedPlayerData.TryGetValue(ServerStaticOwnershipKey, out staticOwnership);
                        if (hasVersion)
                        {
                            handshakeValid = true;
                            active = !hasStaticOwnership || string.Equals(staticOwnership, "True", StringComparison.OrdinalIgnoreCase);
                            extendedTerrain = TryReadSyncedBoolean(serverPeer, ServerTerrainLimitsEnabledKey);
                            terrainRaiseLimit = ReadSyncedTerrainLimit(serverPeer, ServerTerrainRaiseLimitKey, extendedTerrain);
                            terrainDigLimit = ReadSyncedTerrainLimit(serverPeer, ServerTerrainDigLimitKey, extendedTerrain);
                        }
                    }
                    else if (_serverCompanionRpcDetected)
                    {
                        // Legacy servers only provide ownership metadata through RPC.
                        // Keep that compatibility path, but never let it override richer
                        // server-synced terrain metadata when the latter is available.
                        version = _detectedServerCompanionVersion;
                        staticOwnership = _serverCompanionRpcStaticOwnership.ToString();
                        active = _serverCompanionRpcStaticOwnership;
                        handshakeValid = true;
                    }
                }
                else
                {
                    // Do not carry a prior server's terrain authority through a
                    // reconnect or a server-transition gap.
                    ClearCompanionRpcDetection();
                }
            }

            _serverCompanionModeActive = active;
            _serverCompanionHandshakeValid = handshakeValid;
            _detectedServerCompanionVersion = version ?? "";
            _serverExtendedTerrain = extendedTerrain;
            _serverTerrainRaiseLimit = terrainRaiseLimit;
            _serverTerrainDigLimit = terrainDigLimit;
            if (_serverCompanionModeActive && !_serverCompanionModeLogged)
            {
                _serverCompanionModeLogged = true;
                Logger.LogInfo("TerramizerServer companion mode active" +
                               (_detectedServerCompanionVersion.Length > 0 ? " from server version " + _detectedServerCompanionVersion : " by manual override") +
                               (_serverCompanionRpcDetected ? " via RPC" : "") +
                               ". WearNTear and smoke remain on vanilla update cadence. Remote terrain authority=" +
                               (_serverCompanionHandshakeValid && _serverExtendedTerrain ?
                                (_serverTerrainRaiseLimit.ToString("F1") + "m raise/" + _serverTerrainDigLimit.ToString("F1") + "m dig") :
                                "vanilla 8m") + ".");
            }
            else if (!_serverCompanionModeActive && _logServerCompanionDetection.Value && now >= _nextServerCompanionDiagnosticTime && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                _nextServerCompanionDiagnosticTime = now + 30f;
                Logger.LogInfo("TerramizerServer companion detection waiting: serverPeer=" + hasServerPeer +
                               ", rpcDetected=" + rpcDetected +
                               ", syncedKeys=" + syncedKeyCount +
                               ", version='" + (version ?? "") +
                               "', staticOwnership='" + (staticOwnership ?? "") +
                               "', terrainAuthority=vanilla 8m (no validated TerramizerServer)." );
            }
        }

        private void RegisterCompanionRpcsWhenReady()
        {
            if (ZRoutedRpc.instance == null || object.ReferenceEquals(_registeredRoutedRpcInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register<string, bool, bool>(RpcCompanionMetadata, OnCompanionMetadataLegacy);
            ZRoutedRpc.instance.Register<string, bool, bool, bool, float, float>(RpcCompanionMetadataV2, OnCompanionMetadata);
            _registeredRoutedRpcInstance = ZRoutedRpc.instance;
        }

        private void RequestCompanionMetadataIfDue(ZNetPeer serverPeer, float now)
        {
            if (serverPeer == null || ZRoutedRpc.instance == null || _serverCompanionRpcV2Detected ||
                (_serverCompanionLegacyRequestSent && _serverCompanionV2RequestAttempts >= 3) ||
                now < _nextServerCompanionRpcRequestTime)
            {
                return;
            }

            _nextServerCompanionRpcRequestTime = now + 10f;
            try
            {
                if (!_serverCompanionLegacyRequestSent)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer.m_uid, RpcRequestCompanionMetadata);
                    _serverCompanionLegacyRequestSent = true;
                }
                if (_serverCompanionV2RequestAttempts < 3)
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer.m_uid, RpcRequestCompanionMetadataV2);
                    _serverCompanionV2RequestAttempts++;
                }
            }
            catch (Exception ex)
            {
                if (_logServerCompanionDetection != null && _logServerCompanionDetection.Value && now >= _nextServerCompanionDiagnosticTime)
                {
                    Logger.LogInfo("TerramizerServer companion RPC request is waiting for routing readiness: " + ex.Message);
                }
            }
        }

        private static void OnCompanionMetadata(long sender, string version, bool staticOwnership, bool ownershipCache, bool extendedTerrain, float terrainRaiseLimit, float terrainDigLimit)
        {
            if (!IsCurrentServerSender(sender))
            {
                return;
            }

            _serverCompanionRpcDetected = !string.IsNullOrEmpty(version);
            _serverCompanionRpcV2Detected = _serverCompanionRpcDetected;
            _serverCompanionRpcStaticOwnership = staticOwnership;
            _serverCompanionRpcPeerUid = sender;
            _detectedServerCompanionVersion = version ?? "";
            _serverCompanionRpcExtendedTerrain = extendedTerrain;
            _serverCompanionRpcTerrainRaiseLimit = NormalizeRemoteTerrainLimit(terrainRaiseLimit, extendedTerrain);
            _serverCompanionRpcTerrainDigLimit = NormalizeRemoteTerrainLimit(terrainDigLimit, extendedTerrain);
        }

        private static void OnCompanionMetadataLegacy(long sender, string version, bool staticOwnership, bool ownershipCache)
        {
            if (!IsCurrentServerSender(sender))
            {
                return;
            }

            _serverCompanionRpcDetected = !string.IsNullOrEmpty(version);
            _serverCompanionRpcStaticOwnership = staticOwnership;
            _serverCompanionRpcPeerUid = sender;
            _detectedServerCompanionVersion = version ?? "";
            if (!_serverCompanionRpcV2Detected)
            {
                _serverCompanionRpcExtendedTerrain = false;
                _serverCompanionRpcTerrainRaiseLimit = 8f;
                _serverCompanionRpcTerrainDigLimit = 8f;
            }
        }

        private static bool IsCurrentServerSender(long sender)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer())
            {
                return false;
            }

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            return serverPeer != null && serverPeer.m_uid == sender;
        }

        private static void ClearCompanionRpcDetection()
        {
            _serverCompanionRpcDetected = false;
            _serverCompanionRpcV2Detected = false;
            _serverCompanionLegacyRequestSent = false;
            _serverCompanionV2RequestAttempts = 0;
            _serverCompanionHandshakeValid = false;
            _serverCompanionRpcStaticOwnership = false;
            _serverCompanionRpcPeerUid = 0L;
            _serverCompanionRpcExtendedTerrain = false;
            _serverCompanionRpcTerrainRaiseLimit = 8f;
            _serverCompanionRpcTerrainDigLimit = 8f;
            _serverExtendedTerrain = false;
            _serverTerrainRaiseLimit = 8f;
            _serverTerrainDigLimit = 8f;
            _detectedServerCompanionVersion = "";
            _serverCompanionModeLogged = false;
        }

        private static bool TryReadSyncedBoolean(ZNetPeer serverPeer, string key)
        {
            string value;
            return serverPeer != null && serverPeer.m_serverSyncedPlayerData != null &&
                   serverPeer.m_serverSyncedPlayerData.TryGetValue(key, out value) &&
                   string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        private static float ReadSyncedTerrainLimit(ZNetPeer serverPeer, string key, bool extendedTerrain)
        {
            string value;
            float parsed;
            if (serverPeer != null && serverPeer.m_serverSyncedPlayerData != null &&
                serverPeer.m_serverSyncedPlayerData.TryGetValue(key, out value) &&
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return NormalizeRemoteTerrainLimit(parsed, extendedTerrain);
            }
            return extendedTerrain ? 16f : 8f;
        }

        private static float NormalizeRemoteTerrainLimit(float value, bool extendedTerrain)
        {
            if (!extendedTerrain || float.IsNaN(value) || float.IsInfinity(value))
                return 8f;
            return Mathf.Clamp(value, 16f, 64f);
        }

        private static long TakeCounter(ref long counter)
        {
            long value = counter;
            counter = 0;
            return value;
        }

        private void RemoveRetiredSettings()
        {
            Remove("Clutter", "CoalesceGrassResetBursts", false);
            Remove("Clutter", "GrassResetCoalesceSeconds", 0.25f);
            Remove("Clutter", "GrassResetMaxDeferralSeconds", 0.5f);
            Remove("Clutter", "DeferGrassRebuildAfterTerrain", false);
            Remove("Clutter", "GrassRebuildTerrainSettleSeconds", 0.35f);
            Remove("Effects", "PaceSmokeUpdates", false);
            Remove("Effects", "SmokeMinIntervalSeconds", 0.005f);
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
            Remove("Performance", "RespectFramePressure", true);
            Remove("Performance", "FramePressureMilliseconds", 22f);
            Remove("PlacementEffects", "ReducePlacementEffects", true);
            Remove("PlacementEffects", "PlacementEffectScale", 0.3f);
            Remove("PlacementEffects", "PlacementEffectMinimumScaleDuringBurst", 0.15f);
            Remove("PlacementEffects", "PlacementEffectLifetimeScale", 0.55f);
            Remove("PlacementEffects", "PlacementEffectMaxParticles", 18);
            Remove("ServerCompanion", "ServerCompanionWearNTearMinIntervalSeconds", 0.16f);
            Remove("ServerCompanion", "ServerCompanionSmokeMinIntervalSeconds", 0.005f);
            Remove("ServerCompatibility", "AutoDetectTerramizerServer", true);
            Remove("ServerCompatibility", "LogServerCompatibility", true);
            Remove("Structures", "PaceWearNTearUpdates", true);
            Remove("Structures", "WearNTearMinIntervalSeconds", 0.08f);
            Remove("Terrain", "EnableExtendedTerrainWhenSupported", true);
            Remove("Terrain", "LocalRaiseLimitMeters", 16f);
            Remove("Terrain", "LocalDigLimitMeters", 12f);
            Config.Save();
        }

        private void Remove<T>(string section, string key, T defaultValue)
        {
            ConfigEntry<T> retired = Config.Bind(section, key, defaultValue, "Retired setting.");
            Config.Remove(retired.Definition);
        }
    }
}
