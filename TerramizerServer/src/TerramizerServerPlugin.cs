using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

[assembly: AssemblyTitle(TerramizerServer.TerramizerServerPlugin.PluginName)]
[assembly: AssemblyVersion(TerramizerServer.TerramizerServerPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(TerramizerServer.TerramizerServerPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(TerramizerServer.TerramizerServerPlugin.CreatorCredit)]
[assembly: AssemblyProduct(TerramizerServer.TerramizerServerPlugin.PluginName)]
[assembly: AssemblyCopyright(TerramizerServer.TerramizerServerPlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", TerramizerServer.TerramizerServerPlugin.CreatorCredit)]

namespace TerramizerServer
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class TerramizerServerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.terramizerserver";
        public const string PluginName = "TerramizerServer";
        public const string PluginVersion = "1.0.3";
        public const string CreatorCredit = "Created by R4V9N1";
        private const string SyncedVersionKey = "r4v9n1.terramizerserver.version";
        private const string SyncedStaticOwnershipKey = "r4v9n1.terramizerserver.staticOwnership";
        private const string SyncedOwnershipCacheKey = "r4v9n1.terramizerserver.ownershipCache";
        private const string SyncedTerrainLimitsEnabledKey = "r4v9n1.terramizerserver.extendedTerrainLimits";
        private const string SyncedTerrainRaiseLimitKey = "r4v9n1.terramizerserver.terrainRaiseLimit";
        private const string SyncedTerrainDigLimitKey = "r4v9n1.terramizerserver.terrainDigLimit";
        private const string RpcRequestCompanionMetadata = "r4v9n1.terramizerserver.RequestCompanionMetadata";
        private const string RpcCompanionMetadata = "r4v9n1.terramizerserver.CompanionMetadata";
        private const string RpcRequestCompanionMetadataV2 = "r4v9n1.terramizerserver.RequestCompanionMetadataV2";
        private const string RpcCompanionMetadataV2 = "r4v9n1.terramizerserver.CompanionMetadataV2";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _dedicatedOnly;
        private static ConfigEntry<bool> _disableUnityJobDebugger;
        private static ConfigEntry<bool> _reuseCollisionCallbacks;
        private static ConfigEntry<bool> _enableStaticPieceServerOwnership;
        private static ConfigEntry<bool> _dryRunStaticPieceServerOwnership;
        private static ConfigEntry<bool> _playerBuiltPiecesOnly;
        private static ConfigEntry<bool> _requireWearNTear;
        private static ConfigEntry<float> _ownershipScanIntervalSeconds;
        private static ConfigEntry<int> _maxClaimsPerScan;
        private static ConfigEntry<int> _zdoRecordsPerScan;
        private static ConfigEntry<float> _maintenanceOwnershipScanIntervalSeconds;
        private static ConfigEntry<int> _maintenanceMaxClaimsPerScan;
        private static ConfigEntry<int> _maintenanceZdoRecordsPerScan;
        private static ConfigEntry<bool> _enableBackgroundOwnershipAudit;
        private static ConfigEntry<bool> _enableOwnershipCache;
        private static ConfigEntry<float> _ownershipCacheMaxAgeHours;
        private static ConfigEntry<int> _ownershipCacheRecordsPerScan;
        private static ConfigEntry<int> _maxCacheRestoresPerScan;
        private static ConfigEntry<bool> _resumeZdoScanFromCache;
        private static ConfigEntry<bool> _logDevClaims;
        private static ConfigEntry<bool> _logWatchedClaims;
        private static ConfigEntry<bool> _logDevSkips;
        private static ConfigEntry<float> _devSummaryIntervalSeconds;
        private static ConfigEntry<bool> _speedUpSleepFastForward;
        private static ConfigEntry<float> _sleepFastForwardSeconds;
        private static ConfigEntry<bool> _skipSleepWorldSave;
        private static ConfigEntry<bool> _preserveAutosaveTimer;
        private static ConfigEntry<bool> _logSkippedSleepSaves;
        private static FieldInfo _envManSkipToTimeField;
        private static FieldInfo _envManTimeSkipSpeedField;
        private static FieldInfo _gameSaveTimerField;

        private static ManualLogSource _log;
        private static FieldInfo _objectsByIdField;
        private static int _zdoScanIndex;
        private static int _ownershipCacheScanIndex;
        private static bool _ownershipCacheWarmStartComplete;
        private static bool _ownershipCacheDirty;
        private static bool _serverMetadataAdvertised;
        private static bool _loadedZdoScanCursorPending;
        private static bool _companionRpcReplyLogged;
        private static bool _applicationQuitting;
        private static bool _ownershipPatchFailureLogged;
        private static bool _ownershipMaintenanceFailureLogged;
        private static bool _firstBroadScanStatusStarted;
        private static bool _firstBroadScanStatusCompleted;
        private static float _nextFirstBroadScanStatusTime;
        private static string _ownershipCachePath;
        private static float _nextOwnershipScanTime;
        private static float _nextDevSummaryTime;
        private static float _nextOwnershipCacheFlushTime;
        private static ZNet _metadataAdvertisedForInstance;
        private static int _cachedZdoScanIndex;
        private static int _cachedZdoScanObjectCount;
        private static int _zdoBroadScanPassesCompleted;
        private static long _cachedZdoScanUpdatedUnixSeconds;
        private static long _claimAttempts;
        private static long _claimed;
        private static long _totalClaimed;
        private static long _watchedClaims;
        private static long _totalWatchedClaims;
        private static long _alreadyServerOwned;
        private static long _skippedNotReady;
        private static long _skippedNotPersistent;
        private static long _skippedNoCreator;
        private static long _skippedNoWearNTear;
        private static long _skippedDynamic;
        private static long _skippedNoZdo;
        private static long _skippedNoPrefab;
        private static long _skippedNotPiecePrefab;
        private static long _zdoRecordsChecked;
        private static long _cacheRecordsChecked;
        private static long _cacheRestored;
        private static long _totalCacheRestored;
        private static long _cacheRemoved;
        private static long _companionRpcReplies;
        private static readonly Dictionary<int, bool> _prefabEligibilityCache = new Dictionary<int, bool>();
        private static readonly Dictionary<string, OwnershipCacheEntry> _ownershipCache = new Dictionary<string, OwnershipCacheEntry>();
        private static ZRoutedRpc _registeredRoutedRpcInstance;

        private Harmony _harmony;

        private void Awake()
        {
            _log = Logger;

            _enabled = Config.Bind("General", "Enabled", true,
                "Enable TerramizerServer runtime features.");
            _dedicatedOnly = Config.Bind("General", "DedicatedOnly", true,
                "Apply runtime features only in a batch-mode dedicated-server process.");
            BindTerrainCompatibilityConfig();
            _disableUnityJobDebugger = Config.Bind("Performance", "DisableUnityJobDebugger", true,
                "Disable Unity's development-only job debugger without changing simulation, networking, ownership, objects, or saves.");
            _reuseCollisionCallbacks = Config.Bind("Performance", "ReuseCollisionCallbacks", true,
                "Reuse Unity collision callback objects to reduce physics GC allocations. Disable for mods that retain Collision objects after callbacks.");
            _enableStaticPieceServerOwnership = Config.Bind("ExperimentalOwnership", "EnableStaticPieceServerOwnership", true,
                "EXPERIMENTAL: make the dedicated server claim ownership of loaded, static, player-created structure pieces while preserving their creator field.");
            _dryRunStaticPieceServerOwnership = Config.Bind("ExperimentalOwnership", "DryRunStaticPieceServerOwnership", false,
                "Log which pieces would be claimed without changing ZDO ownership.");
            _playerBuiltPiecesOnly = Config.Bind("ExperimentalOwnership", "PlayerBuiltPiecesOnly", true,
                "Only claim pieces with a non-zero Valheim creator field.");
            _requireWearNTear = Config.Bind("ExperimentalOwnership", "RequireWearNTear", true,
                "Only claim structure-style pieces that have WearNTear. This avoids crops and other non-structural placed objects.");
            _ownershipScanIntervalSeconds = BindRange("ExperimentalOwnership", "OwnershipScanIntervalSeconds", 15f,
                "Seconds between bounded ownership recovery passes. Newly loaded pieces are handled immediately.", 1f, 120f);
            _maxClaimsPerScan = BindRange("ExperimentalOwnership", "MaxClaimsPerScan", 100,
                "Maximum ZDO ownership changes allowed per scan.", 1, 5000);
            _zdoRecordsPerScan = BindRange("ExperimentalOwnership", "ZdoRecordsPerScan", 10000,
                "Maximum persistent ZDO records inspected per recovery pass. This work is bounded to protect active players.", 100, 100000);
            _maintenanceOwnershipScanIntervalSeconds = BindRange("ExperimentalOwnership", "MaintenanceOwnershipScanIntervalSeconds", 1800f,
                "Seconds between infrequent integrity audits after the initial world pass. New/loaded pieces are handled immediately.", 60f, 21600f);
            _maintenanceMaxClaimsPerScan = BindRange("ExperimentalOwnership", "MaintenanceMaxClaimsPerScan", 25,
                "Maximum ZDO ownership changes allowed per maintenance scan after the first full broad pass.", 1, 5000);
            _maintenanceZdoRecordsPerScan = BindRange("ExperimentalOwnership", "MaintenanceZdoRecordsPerScan", 1000,
                "Maximum persistent ZDO records inspected per maintenance scan after the first full broad pass.", 100, 100000);
            _enableBackgroundOwnershipAudit = Config.Bind("ExperimentalOwnership", "EnableBackgroundOwnershipAudit", true,
                "Run the legacy whole-world ZDO ownership audit. Disabled by default because event-driven claims cover newly loaded structures without scanning the live world table.");
            _enableOwnershipCache = Config.Bind("OwnershipCache", "EnableOwnershipCache", true,
                "Store known eligible claimed ZDOs so restart warm-start can quickly reassign them to the new server session id.");
            _ownershipCacheMaxAgeHours = BindRange("OwnershipCache", "OwnershipCacheMaxAgeHours", 168f,
                "Maximum age in hours for remembered ownership records. Older cache rows are ignored and removed.", 1f, 2160f);
            _ownershipCacheRecordsPerScan = BindRange("OwnershipCache", "OwnershipCacheRecordsPerScan", 200000,
                "Maximum cached ownership records inspected per restart warm-start pass.", 1000, 1000000);
            _maxCacheRestoresPerScan = BindRange("OwnershipCache", "MaxCacheRestoresPerScan", 5000,
                "Maximum cached ownership restores applied per warm-start pass.", 1, 50000);
            _resumeZdoScanFromCache = Config.Bind("OwnershipCache", "ResumeZdoScanFromCache", true,
                "Resume the broad ZDO eligibility scan from the last persisted scan index after server restart. The cache clamps this index if the world ZDO count changed.");
            _logDevClaims = Config.Bind("DevLogs", "LogOwnershipClaims", false,
                "Log every static piece ownership claim or dry-run claim. This is very noisy on established worlds.");
            _logWatchedClaims = Config.Bind("DevLogs", "LogWatchedOwnershipClaims", false,
                "Log ownership claims for sensitive interactive prefabs such as beds, chests, portals, fires, stations, signs, item stands, wards, doors, and gates.");
            _logDevSkips = Config.Bind("DevLogs", "LogOwnershipSkips", false,
                "Log skipped pieces. This is noisy and should only be enabled during focused testing.");
            _devSummaryIntervalSeconds = BindRange("DevLogs", "OwnershipSummaryIntervalSeconds", 60f,
                "Seconds between compact ownership experiment summaries.", 5f, 600f);
            _speedUpSleepFastForward = Config.Bind("Sleep", "SpeedUpSleepFastForward", true, "Tune sleep time-skip to the configured real-time duration.");
            _sleepFastForwardSeconds = BindRange("Sleep", "SleepFastForwardSeconds", 4f, "Target real seconds for sleep time-skip.", 1f, 60f);
            _skipSleepWorldSave = Config.Bind("Sleep", "SkipSleepWorldSave", true, "Skip the extra world save triggered when sleep ends.");
            _preserveAutosaveTimer = Config.Bind("Sleep", "PreserveAutosaveTimer", true, "Keep the regular autosave timer unchanged when the sleep save is skipped.");
            _logSkippedSleepSaves = Config.Bind("Sleep", "LogSkippedSleepSaves", true, "Log when the sleep-triggered world save is skipped.");
            InitializeStreaming();
            RemoveRetiredSettings();
            _envManSkipToTimeField = AccessTools.Field(typeof(EnvMan), "m_skipToTime");
            _envManTimeSkipSpeedField = AccessTools.Field(typeof(EnvMan), "m_timeSkipSpeed");
            _gameSaveTimerField = AccessTools.Field(typeof(Game), "m_saveTimer");

            // The legacy cache belongs to the optional whole-world audit. Normal
            // operation is event-driven and must not read or maintain a world-sized
            // cache on the dedicated server.
            if (_enableBackgroundOwnershipAudit.Value)
            {
                LoadOwnershipCache();
            }
            ApplySafeRuntimeSetting();
            InstallExperimentalPatches();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Logger.LogInfo("Static piece server ownership experiment: enabled=" + _enableStaticPieceServerOwnership.Value +
                           ", dryRun=" + _dryRunStaticPieceServerOwnership.Value +
                           ", playerBuiltOnly=" + _playerBuiltPiecesOnly.Value +
                           ", requireWearNTear=" + _requireWearNTear.Value + ".");
            Logger.LogInfo("Creator fields are preserved. WearNTear is not removed. Dynamic physics objects remain peer-owned.");
            Logger.LogInfo("Bounded zone streaming prefetch: enabled=" + _streamingBoostEnabled.Value +
                           ", maxZdosPerPeer=" + _streamingBoostMaxZdos.Value +
                           ", cooldown=" + _streamingBoostCooldown.Value.ToString("F2") + "s, socket backpressure=" + _streamingBoostMaxQueuePercent.Value + "%. Peer-zone scene creation remains disabled.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private ConfigEntry<T> BindRange<T>(string section, string key, T defaultValue, string description, T minimum, T maximum) where T : System.IComparable
        {
            return Config.Bind(section, key, defaultValue, new ConfigDescription(description, new AcceptableValueRange<T>(minimum, maximum)));
        }

        private void ApplySafeRuntimeSetting()
        {
            if (!_enabled.Value || (_dedicatedOnly.Value && !UnityEngine.Application.isBatchMode))
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

                Logger.LogInfo("Unity's development job debugger is disabled; all server game systems remain unmodified.");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("Could not change Unity's optional job-debugger setting; continuing with vanilla behavior: " + ex.Message);
            }
        }

        private void InstallExperimentalPatches()
        {
            if (!_enabled.Value || (_dedicatedOnly.Value && !Application.isBatchMode))
            {
                return;
            }

            _harmony = new Harmony(PluginGuid);
            BinarySearchDictionarySetValuePatch.Install(_harmony, Logger.LogInfo);
            TryPatch(typeof(VisEquipmentIntCachePatch), "VisEquipment ZDO integer lookup cache");
            TryPatch(typeof(ZPackageWritePackagePatch), "allocation-free ZPackage nesting");
            TryPatch(typeof(ZdoOwnershipHandoffPatch), "optimized ZDO ownership handoff scan");
            TryPatch(typeof(ZNetViewAwakeStaticOwnershipPatch), "ZNetView creation ownership observation");
            TryPatch(typeof(EnvManSkipToMorningPatch), "sleep fast-forward");
            TryPatch(typeof(GameSleepStopPatch), "sleep save policy");
            TryPatch(typeof(HeightmapAtMaxWorldLevelDepthTerrainLimitPatch), "world heightmap dig-depth terrain limit");
            TryPatch(typeof(HeightmapLevelTerrainTerrainLimitPatch), "heightmap terrain raise/dig limits");
            TryPatch(typeof(TerrainCompLevelTerrainLimitPatch), "terrain component raise/dig limits");
            TryPatch(typeof(TerrainCompRaiseTerrainLimitPatch), "terrain component direct raise limit");
            TryPatch(typeof(TerrainCompApplyToHeightmapTerrainLimitPatch), "terrain final-apply limits");
            TryPatch(typeof(AudioManHeadlessPatch), "headless audio guard");
            TryPatch(typeof(ShieldDomeAwakeHeadlessPatch), "headless shield initialization guard");
            TryPatch(typeof(ShieldDomeColorHeadlessPatch), "headless shield color guard");
        }

        private void TryPatch(System.Type patchType, string name)
        {
            try
            {
                _harmony.CreateClassProcessor(patchType).Patch();
                Logger.LogInfo("Installed " + name + ".");
            }
            catch (System.Exception ex)
            {
                Logger.LogWarning("Could not install " + name + "; continuing without that patch: " + ex.Message);
            }
        }

        internal static bool ShouldSkipHeadlessVisualSystems()
        {
            return Application.isBatchMode;
        }

        private void Update()
        {
            RegisterCompanionRpcsWhenReady();
            AdvertiseServerMetadata();
            RunStreamingBoost();

            if (!ShouldRunExperiment())
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_enableBackgroundOwnershipAudit.Value && _enableOwnershipCache.Value && !_ownershipCacheWarmStartComplete)
            {
                try
                {
                    ApplyOwnershipCacheWarmStart();
                }
                catch (System.Exception ex)
                {
                    LogOwnershipMaintenanceFailure(ex);
                }
            }

            if (_enableBackgroundOwnershipAudit.Value && now >= _nextOwnershipScanTime)
            {
                try
                {
                    SweepLoadedPieces();
                }
                catch (System.Exception ex)
                {
                    LogOwnershipMaintenanceFailure(ex);
                }
                _nextOwnershipScanTime = now + GetOwnershipScanIntervalSeconds();
            }

            if (_enableBackgroundOwnershipAudit.Value && now >= _nextDevSummaryTime)
            {
                _nextDevSummaryTime = now + _devSummaryIntervalSeconds.Value;
                LogOwnershipSummary();
            }

            if (_ownershipCacheDirty && now >= _nextOwnershipCacheFlushTime)
            {
                FlushOwnershipCache(force: false);
            }
        }

        private void OnDestroy()
        {
            FlushOwnershipCache(force: true);

            if (_harmony != null && !_applicationQuitting)
            {
                try
                {
                    _harmony.UnpatchSelf();
                }
                catch (System.Exception ex)
                {
                    Logger.LogWarning("Could not unpatch TerramizerServer during teardown; process shutdown can continue safely: " + ex.Message);
                }
                _harmony = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            FlushOwnershipCache(force: true);
        }

        internal static void TryClaimFromZNetView(ZNetView view, string reason)
        {
            try
            {
                if (!ShouldRunExperiment() || view == null)
                {
                    return;
                }

                ZDO zdo = view.GetZDO();
                if (zdo == null)
                {
                    return;
                }

                // Most ZNetViews are creatures, items, effects, or other
                // non-structure objects. Use the persistent prefab hash and a
                // cached prefab eligibility result to avoid component-tree
                // walks for those objects during every zone load.
                if (ZNetScene.instance != null)
                {
                    int prefabHash = zdo.GetPrefab();
                    GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
                    if (prefab != null && !IsEligibleStaticPiecePrefab(prefabHash, prefab))
                    {
                        return;
                    }
                }

                Piece piece = view.GetComponent<Piece>();
                if (piece == null)
                {
                    piece = view.GetComponentInChildren<Piece>(true);
                }

                if (piece == null)
                {
                    return;
                }

                TryClaimPiece(piece, reason, allowClaimLimit: false, claimsThisScan: null);
            }
            catch (System.Exception ex)
            {
                // Ownership is an optional optimization. Never allow a failed
                // eligibility/ownership probe to break vanilla object creation.
                if (!_ownershipPatchFailureLogged && _log != null)
                {
                    _ownershipPatchFailureLogged = true;
                    _log.LogWarning("Static ownership observation failed; the affected object remains vanilla-owned: " + ex.Message);
                }
            }
        }

        private static void SweepLoadedPieces()
        {
            Piece[] pieces = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None);
            int claims = 0;
            int maxClaims = GetMaxClaimsPerScan();
            for (int i = 0; i < pieces.Length; i++)
            {
                if (claims >= maxClaims)
                {
                    break;
                }

                int before = claims;
                TryClaimPiece(pieces[i], "sweep", allowClaimLimit: true, claimsThisScan: delegate { claims++; });
                if (claims != before && _dryRunStaticPieceServerOwnership.Value)
                {
                    claims = before + 1;
                }
            }

            if (claims < maxClaims)
            {
                SweepZdoRecords(maxClaims - claims);
            }
        }

        private static void TryClaimPiece(Piece piece, string reason, bool allowClaimLimit, System.Action claimsThisScan)
        {
            _claimAttempts++;

            if (piece == null || piece.gameObject == null)
            {
                _skippedNotReady++;
                return;
            }

            ZNetView view = piece.GetComponent<ZNetView>();
            if (view == null)
            {
                view = piece.GetComponentInParent<ZNetView>();
            }
            if (view == null)
            {
                view = piece.GetComponentInChildren<ZNetView>(true);
            }

            if (view == null || !view.IsValid() || view.GetZDO() == null)
            {
                _skippedNoZdo++;
                LogSkip(piece, "no valid ZDO");
                return;
            }

            ZDO zdo = view.GetZDO();
            if (!zdo.Persistent)
            {
                _skippedNotPersistent++;
                LogSkip(piece, "not persistent");
                return;
            }

            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (_playerBuiltPiecesOnly.Value && creator == 0L)
            {
                _skippedNoCreator++;
                LogSkip(piece, "no player creator");
                return;
            }

            if (_requireWearNTear.Value && piece.GetComponent<WearNTear>() == null && piece.GetComponentInChildren<WearNTear>(true) == null)
            {
                _skippedNoWearNTear++;
                LogSkip(piece, "no WearNTear");
                return;
            }

            if (IsDynamicOrPhysicsObject(piece.gameObject))
            {
                _skippedDynamic++;
                LogSkip(piece, "dynamic or physics object");
                return;
            }

            string prefabName = CleanPrefabName(piece.gameObject.name);
            long serverId = ZDOMan.GetSessionID();
            long oldOwner = zdo.GetOwner();
            if (oldOwner == serverId)
            {
                _alreadyServerOwned++;
                RecordOwnershipCache(zdo, prefabName, creator);
                return;
            }

            if (_dryRunStaticPieceServerOwnership.Value)
            {
                _claimed++;
                _totalClaimed++;
                if (claimsThisScan != null)
                {
                    claimsThisScan();
                }
                LogClaim("DRY-RUN would claim", prefabName, zdo, oldOwner, creator, reason);
                return;
            }

            zdo.SetOwner(serverId);
            if (ZDOMan.instance != null)
            {
                ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            }

            _claimed++;
            _totalClaimed++;
            RecordOwnershipCache(zdo, prefabName, creator);
            if (claimsThisScan != null)
            {
                claimsThisScan();
            }
            LogClaim("Claimed", prefabName, zdo, oldOwner, creator, reason);
        }

        private static void SweepZdoRecords(int remainingClaimBudget)
        {
            if (remainingClaimBudget <= 0 || ZDOMan.instance == null || ZNetScene.instance == null)
            {
                return;
            }

            Dictionary<ZDOID, ZDO> objects = GetObjectsById();
            if (objects == null || objects.Count == 0)
            {
                return;
            }

            ApplyCachedZdoScanCursor(objects.Count);
            ValidateCachedScanPassState(objects.Count);
            LogFirstBroadScanStatus(objects.Count, completed: false);
            if (_zdoScanIndex >= objects.Count)
            {
                _zdoScanIndex = 0;
            }

            int index = 0;
            int processed = 0;
            int claimedThisScan = 0;
            int maxRecords = GetZdoRecordsPerScan();

            try
            {
                foreach (KeyValuePair<ZDOID, ZDO> pair in objects)
                {
                    if (index++ < _zdoScanIndex)
                    {
                        continue;
                    }
                    if (processed >= maxRecords || claimedThisScan >= remainingClaimBudget)
                    {
                        break;
                    }

                    processed++;
                    _zdoRecordsChecked++;
                    if (TryClaimZdoRecord(pair.Value, "zdo-sweep"))
                    {
                        claimedThisScan++;
                    }
                }
            }
            catch (System.InvalidOperationException ex)
            {
                _zdoScanIndex = 0;
                if (_log != null)
                {
                    _log.LogWarning("[dev ownership] ZDO scan restarted because the ZDO table changed during enumeration: " + ex.Message);
                }
                return;
            }

            bool completedPass = _zdoScanIndex + processed >= objects.Count;
            _zdoScanIndex += processed;
            if (completedPass || processed == 0)
            {
                _zdoScanIndex = 0;
                if (completedPass && processed > 0)
                {
                    MarkBroadZdoScanPassComplete(objects.Count);
                }
            }
            PersistZdoScanCursor(objects.Count);
            LogFirstBroadScanStatus(objects.Count, completed: completedPass && processed > 0);
        }

        private static void LogFirstBroadScanStatus(int objectCount, bool completed)
        {
            if (_log == null || !_enableBackgroundOwnershipAudit.Value || _zdoBroadScanPassesCompleted > 0 && _firstBroadScanStatusCompleted)
                return;

            float now = Time.realtimeSinceStartup;
            if (!completed && _firstBroadScanStatusStarted && now < _nextFirstBroadScanStatusTime)
                return;

            if (!_firstBroadScanStatusStarted)
            {
                _firstBroadScanStatusStarted = true;
                float configuredRate = GetZdoRecordsPerScan() / Math.Max(1f, GetOwnershipScanIntervalSeconds());
                float referenceRate = 5000f;
                float estimatedSeconds = objectCount / Math.Max(1f, configuredRate);
                _nextFirstBroadScanStatusTime = now + 30f;
                _log.LogInfo("[ownership scan] First broad ZDO scan started: " + objectCount +
                             " records; estimated time " + FormatScanDuration(estimatedSeconds) +
                             " at current budget (" + configuredRate.ToString("F0") + " records/s)." +
                             " Prior measured warm-up reference was " + referenceRate.ToString("F0") + " records/s with a 25,000/5s budget.");
                return;
            }

            if (completed)
            {
                _firstBroadScanStatusCompleted = true;
                _log.LogInfo("[ownership scan] First broad ZDO scan completed: " + objectCount + " records inspected. Switching to maintenance mode.");
                return;
            }

            int processed = Math.Min(Math.Max(_zdoScanIndex, 0), objectCount);
            int remaining = Math.Max(0, objectCount - processed);
            float rate = GetZdoRecordsPerScan() / Math.Max(1f, GetOwnershipScanIntervalSeconds());
            float estimatedRemaining = remaining / Math.Max(1f, rate);
            float percent = objectCount > 0 ? processed * 100f / objectCount : 100f;
            _nextFirstBroadScanStatusTime = now + 30f;
            _log.LogInfo("[ownership scan] First broad scan progress: " + percent.ToString("F1") +
                         "% (" + processed + "/" + objectCount + "); estimated remaining " +
                         FormatScanDuration(estimatedRemaining) + ".");
        }

        private static string FormatScanDuration(float seconds)
        {
            if (seconds < 60f) return Math.Max(0, (int)Math.Ceiling(seconds)) + " seconds";
            int minutes = (int)(seconds / 60f);
            int hours = minutes / 60;
            minutes %= 60;
            return hours > 0 ? hours + "h " + minutes + "m" : minutes + "m";
        }

        private static bool IsMaintenanceScanMode()
        {
            return _zdoBroadScanPassesCompleted > 0 && (_ownershipCacheWarmStartComplete || _enableOwnershipCache == null || !_enableOwnershipCache.Value);
        }

        private static float GetOwnershipScanIntervalSeconds()
        {
            if (IsMaintenanceScanMode() && _maintenanceOwnershipScanIntervalSeconds != null)
            {
                return _maintenanceOwnershipScanIntervalSeconds.Value;
            }

            return _ownershipScanIntervalSeconds != null ? _ownershipScanIntervalSeconds.Value : 5f;
        }

        private static int GetMaxClaimsPerScan()
        {
            if (IsMaintenanceScanMode() && _maintenanceMaxClaimsPerScan != null)
            {
                return _maintenanceMaxClaimsPerScan.Value;
            }

            return _maxClaimsPerScan != null ? _maxClaimsPerScan.Value : 250;
        }

        private static int GetZdoRecordsPerScan()
        {
            if (IsMaintenanceScanMode() && _maintenanceZdoRecordsPerScan != null)
            {
                return _maintenanceZdoRecordsPerScan.Value;
            }

            return _zdoRecordsPerScan != null ? _zdoRecordsPerScan.Value : 25000;
        }

        private static void MarkBroadZdoScanPassComplete(int objectCount)
        {
            _zdoBroadScanPassesCompleted++;
            _cachedZdoScanObjectCount = objectCount;
            _cachedZdoScanUpdatedUnixSeconds = GetUnixSeconds();
            _ownershipCacheDirty = true;

            if (_zdoBroadScanPassesCompleted == 1 && _log != null)
            {
                _log.LogInfo("[dev ownership] Broad ZDO scan completed first full pass; switching to maintenance scan mode. interval=" +
                             GetOwnershipScanIntervalSeconds() +
                             "s, maxClaims=" + GetMaxClaimsPerScan() +
                             ", zdoRecordsPerScan=" + GetZdoRecordsPerScan() + ".");
            }
        }

        private static Dictionary<ZDOID, ZDO> GetObjectsById()
        {
            if (_objectsByIdField == null)
            {
                _objectsByIdField = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");
                if (_objectsByIdField == null)
                {
                    if (_log != null)
                    {
                        _log.LogWarning("[dev ownership] Could not find ZDOMan.m_objectsByID; direct ZDO ownership scan is unavailable.");
                    }
                    return null;
                }
            }

            return _objectsByIdField.GetValue(ZDOMan.instance) as Dictionary<ZDOID, ZDO>;
        }

        private static void ApplyCachedZdoScanCursor(int currentObjectCount)
        {
            if (!_loadedZdoScanCursorPending || _cachedZdoScanIndex <= 0 || _resumeZdoScanFromCache == null || !_resumeZdoScanFromCache.Value)
            {
                return;
            }

            int restoredIndex = _cachedZdoScanIndex;
            if (currentObjectCount > 0)
            {
                restoredIndex = Mathf.Clamp(restoredIndex, 0, currentObjectCount - 1);
            }

            _zdoScanIndex = restoredIndex;
            if (_log != null)
            {
                _log.LogInfo("[ownership cache] Resuming broad ZDO scan at index " + _zdoScanIndex +
                             " from cached index " + _cachedZdoScanIndex +
                             " with currentObjectCount=" + currentObjectCount +
                             ", cachedObjectCount=" + _cachedZdoScanObjectCount + ".");
            }

            _loadedZdoScanCursorPending = false;
            _cachedZdoScanIndex = 0;
        }

        private static void ValidateCachedScanPassState(int currentObjectCount)
        {
            if (_zdoBroadScanPassesCompleted <= 0 || _cachedZdoScanObjectCount <= 0 || currentObjectCount <= 0)
            {
                return;
            }

            int delta = Mathf.Abs(currentObjectCount - _cachedZdoScanObjectCount);
            int allowedDelta = Mathf.Max(10000, _cachedZdoScanObjectCount / 10);
            if (delta <= allowedDelta)
            {
                return;
            }

            if (_log != null)
            {
                _log.LogInfo("[ownership cache] Cached broad-scan pass count was reset because the world ZDO count changed from " +
                             _cachedZdoScanObjectCount + " to " + currentObjectCount + ".");
            }

            _zdoBroadScanPassesCompleted = 0;
            _cachedZdoScanObjectCount = currentObjectCount;
            _cachedZdoScanUpdatedUnixSeconds = GetUnixSeconds();
            _ownershipCacheDirty = true;
        }

        private static void PersistZdoScanCursor(int objectCount)
        {
            if (_enableOwnershipCache == null || !_enableOwnershipCache.Value || _resumeZdoScanFromCache == null || !_resumeZdoScanFromCache.Value)
            {
                return;
            }

            _cachedZdoScanIndex = _zdoScanIndex;
            _cachedZdoScanObjectCount = objectCount;
            _cachedZdoScanUpdatedUnixSeconds = GetUnixSeconds();
            _ownershipCacheDirty = true;
            if (_nextOwnershipCacheFlushTime <= 0f)
            {
                _nextOwnershipCacheFlushTime = Time.realtimeSinceStartup + 30f;
            }
        }

        private static void ApplyOwnershipCacheWarmStart()
        {
            if (_ownershipCacheWarmStartComplete || !_enableOwnershipCache.Value)
            {
                return;
            }

            if (_ownershipCache.Count == 0)
            {
                _ownershipCacheWarmStartComplete = true;
                return;
            }

            Dictionary<ZDOID, ZDO> objects = GetObjectsById();
            if (objects == null || objects.Count == 0)
            {
                return;
            }

            if (_ownershipCacheScanIndex >= _ownershipCache.Count)
            {
                _ownershipCacheScanIndex = 0;
            }

            List<OwnershipCacheEntry> entries = new List<OwnershipCacheEntry>(_ownershipCache.Values);
            int index = 0;
            int processed = 0;
            int restored = 0;
            int maxRecords = _ownershipCacheRecordsPerScan.Value;
            int maxRestores = _maxCacheRestoresPerScan.Value;
            List<string> removeAfterPass = null;

            foreach (OwnershipCacheEntry entry in entries)
            {
                if (index++ < _ownershipCacheScanIndex)
                {
                    continue;
                }
                if (processed >= maxRecords || restored >= maxRestores)
                {
                    break;
                }

                processed++;
                _cacheRecordsChecked++;

                ZDOID id;
                if (!TryParseZdoId(entry.Uid, out id))
                {
                    if (removeAfterPass == null)
                    {
                        removeAfterPass = new List<string>();
                    }
                    removeAfterPass.Add(entry.Uid);
                    continue;
                }

                ZDO zdo;
                if (!objects.TryGetValue(id, out zdo))
                {
                    continue;
                }

                if (TryRestoreCachedOwnership(zdo))
                {
                    restored++;
                }
            }

            if (removeAfterPass != null)
            {
                for (int i = 0; i < removeAfterPass.Count; i++)
                {
                    RemoveOwnershipCacheEntry(removeAfterPass[i]);
                }
            }

            _ownershipCacheScanIndex += processed;
            if (_ownershipCacheScanIndex >= entries.Count || processed == 0)
            {
                _ownershipCacheWarmStartComplete = true;
                _ownershipCacheScanIndex = 0;
                if (_log != null)
                {
                    _log.LogInfo("[ownership cache] Direct warm-start complete. cacheEntries=" + _ownershipCache.Count +
                                 ", totalCacheRestored=" + _totalCacheRestored +
                                 ", removed=" + _cacheRemoved + ".");
                }
                FlushOwnershipCache(force: false);
            }
        }

        private static bool TryRestoreCachedOwnership(ZDO zdo)
        {
            if (zdo == null || !zdo.IsValid())
            {
                return false;
            }

            string uid = zdo.m_uid.ToString();
            OwnershipCacheEntry entry;
            if (!_ownershipCache.TryGetValue(uid, out entry))
            {
                return false;
            }

            if (!zdo.Persistent || zdo.GetPrefab() != entry.PrefabHash)
            {
                RemoveOwnershipCacheEntry(uid);
                return false;
            }

            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (_playerBuiltPiecesOnly.Value && creator == 0L)
            {
                RemoveOwnershipCacheEntry(uid);
                return false;
            }
            if (entry.Creator != 0L && creator != entry.Creator)
            {
                RemoveOwnershipCacheEntry(uid);
                return false;
            }

            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(entry.PrefabHash) : null;
            if (prefab != null && !IsEligibleStaticPiecePrefab(entry.PrefabHash, prefab))
            {
                RemoveOwnershipCacheEntry(uid);
                return false;
            }

            long serverId = ZDOMan.GetSessionID();
            long oldOwner = zdo.GetOwner();
            if (oldOwner == serverId)
            {
                RecordOwnershipCache(zdo, entry.PrefabName, creator);
                return false;
            }

            if (_dryRunStaticPieceServerOwnership.Value)
            {
                _claimed++;
                _totalClaimed++;
                _cacheRestored++;
                _totalCacheRestored++;
                LogClaim("DRY-RUN cache would claim", entry.PrefabName, zdo, oldOwner, creator, "ownership-cache");
                return true;
            }

            zdo.SetOwner(serverId);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            _claimed++;
            _totalClaimed++;
            _cacheRestored++;
            _totalCacheRestored++;
            RecordOwnershipCache(zdo, entry.PrefabName, creator);
            LogClaim("Cache-claimed", entry.PrefabName, zdo, oldOwner, creator, "ownership-cache");
            return true;
        }

        private static bool TryParseZdoId(string uid, out ZDOID id)
        {
            id = ZDOID.None;
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            int separator = uid.IndexOf(':');
            if (separator <= 0 || separator >= uid.Length - 1)
            {
                return false;
            }

            long userId;
            uint objectId;
            if (!long.TryParse(uid.Substring(0, separator), out userId) ||
                !uint.TryParse(uid.Substring(separator + 1), out objectId))
            {
                return false;
            }

            id = new ZDOID(userId, objectId);
            return true;
        }

        private static void RecordOwnershipCache(ZDO zdo, string prefabName, long creator)
        {
            if (_enableBackgroundOwnershipAudit == null || !_enableBackgroundOwnershipAudit.Value ||
                _enableOwnershipCache == null || !_enableOwnershipCache.Value || _dryRunStaticPieceServerOwnership.Value || zdo == null || !zdo.IsValid())
            {
                return;
            }

            int prefabHash = zdo.GetPrefab();
            if (prefabHash == 0)
            {
                return;
            }

            string uid = zdo.m_uid.ToString();
            _ownershipCache[uid] = new OwnershipCacheEntry
            {
                Uid = uid,
                PrefabHash = prefabHash,
                Creator = creator,
                PrefabName = CleanPrefabName(prefabName),
                StoredUnixSeconds = GetUnixSeconds()
            };
            _ownershipCacheDirty = true;
            if (_nextOwnershipCacheFlushTime <= 0f)
            {
                _nextOwnershipCacheFlushTime = Time.realtimeSinceStartup + 30f;
            }
        }

        private static void RemoveOwnershipCacheEntry(string uid)
        {
            if (_ownershipCache.Remove(uid))
            {
                _cacheRemoved++;
                _ownershipCacheDirty = true;
            }
        }

        private static void LoadOwnershipCache()
        {
            _ownershipCacheWarmStartComplete = true;
            if (_enableOwnershipCache == null || !_enableOwnershipCache.Value)
            {
                return;
            }

            _ownershipCachePath = System.IO.Path.Combine(Paths.ConfigPath, PluginGuid + ".ownership-cache.tsv");
            _ownershipCacheWarmStartComplete = false;
            if (!System.IO.File.Exists(_ownershipCachePath))
            {
                if (_log != null)
                {
                    _log.LogInfo("[ownership cache] No ownership cache found yet. It will be created as pieces are claimed.");
                }
                return;
            }

            long now = GetUnixSeconds();
            long maxAgeSeconds = (long)(_ownershipCacheMaxAgeHours.Value * 3600f);
            int loaded = 0;
            int expired = 0;

            try
            {
                string[] lines = System.IO.File.ReadAllLines(_ownershipCachePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }
                    if (line[0] == '#')
                    {
                        ReadOwnershipCacheMetadata(line);
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    int prefabHash;
                    long creator;
                    long stored;
                    if (!int.TryParse(parts[1], out prefabHash) ||
                        !long.TryParse(parts[2], out creator) ||
                        !long.TryParse(parts[4], out stored))
                    {
                        continue;
                    }

                    if (now - stored > maxAgeSeconds)
                    {
                        expired++;
                        continue;
                    }

                    _ownershipCache[parts[0]] = new OwnershipCacheEntry
                    {
                        Uid = parts[0],
                        PrefabHash = prefabHash,
                        Creator = creator,
                        PrefabName = parts[3],
                        StoredUnixSeconds = stored
                    };
                    loaded++;
                }
            }
            catch (System.Exception ex)
            {
                _ownershipCache.Clear();
                _ownershipCacheWarmStartComplete = true;
                if (_log != null)
                {
                    _log.LogWarning("[ownership cache] Could not load ownership cache; continuing with normal scan: " + ex.Message);
                }
                return;
            }

            if (expired > 0)
            {
                _ownershipCacheDirty = true;
                _cacheRemoved += expired;
            }
            if (_cachedZdoScanUpdatedUnixSeconds > 0L && now - _cachedZdoScanUpdatedUnixSeconds > maxAgeSeconds)
            {
                _cachedZdoScanIndex = 0;
                _cachedZdoScanObjectCount = 0;
                _zdoBroadScanPassesCompleted = 0;
                _cachedZdoScanUpdatedUnixSeconds = 0L;
                _ownershipCacheDirty = true;
            }
            if (_zdoBroadScanPassesCompleted < 0)
            {
                _zdoBroadScanPassesCompleted = 0;
                _ownershipCacheDirty = true;
            }
            _loadedZdoScanCursorPending = _cachedZdoScanIndex > 0;

            if (_log != null)
            {
                _log.LogInfo("[ownership cache] Loaded " + loaded + " cached ownership records from " + _ownershipCachePath +
                             (expired > 0 ? " and expired " + expired + "." : ".") +
                             " scanIndex=" + _cachedZdoScanIndex +
                             ", scanObjectCount=" + _cachedZdoScanObjectCount +
                             ", scanPassesCompleted=" + _zdoBroadScanPassesCompleted + ".");
            }
        }

        private static void FlushOwnershipCache(bool force)
        {
            if (_enableOwnershipCache == null || !_enableOwnershipCache.Value || string.IsNullOrEmpty(_ownershipCachePath) || (!_ownershipCacheDirty && !force))
            {
                return;
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(_ownershipCachePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                string tempPath = _ownershipCachePath + ".tmp";
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(tempPath, false, new System.Text.UTF8Encoding(false)))
                {
                    writer.WriteLine("# TerramizerServer ownership cache v1");
                    writer.WriteLine("# scanIndex\t" + _cachedZdoScanIndex);
                    writer.WriteLine("# scanObjectCount\t" + _cachedZdoScanObjectCount);
                    writer.WriteLine("# scanPassesCompleted\t" + _zdoBroadScanPassesCompleted);
                    writer.WriteLine("# scanUpdatedUnixSeconds\t" + _cachedZdoScanUpdatedUnixSeconds);
                    writer.WriteLine("# uid\tprefabHash\tcreator\tprefabName\tstoredUnixSeconds");
                    foreach (OwnershipCacheEntry entry in _ownershipCache.Values)
                    {
                        writer.Write(entry.Uid);
                        writer.Write('\t');
                        writer.Write(entry.PrefabHash);
                        writer.Write('\t');
                        writer.Write(entry.Creator);
                        writer.Write('\t');
                        writer.Write(CleanPrefabName(entry.PrefabName));
                        writer.Write('\t');
                        writer.WriteLine(entry.StoredUnixSeconds);
                    }
                }

                if (System.IO.File.Exists(_ownershipCachePath))
                {
                    System.IO.File.Delete(_ownershipCachePath);
                }
                System.IO.File.Move(tempPath, _ownershipCachePath);
                _ownershipCacheDirty = false;
                _nextOwnershipCacheFlushTime = Time.realtimeSinceStartup + 30f;
            }
            catch (System.Exception ex)
            {
                _nextOwnershipCacheFlushTime = Time.realtimeSinceStartup + 60f;
                if (_log != null)
                {
                    _log.LogWarning("[ownership cache] Could not save ownership cache; will retry later: " + ex.Message);
                }
            }
        }

        private static void ReadOwnershipCacheMetadata(string line)
        {
            if (line.StartsWith("# scanIndex\t", System.StringComparison.Ordinal))
            {
                int.TryParse(line.Substring("# scanIndex\t".Length), out _cachedZdoScanIndex);
                return;
            }
            if (line.StartsWith("# scanObjectCount\t", System.StringComparison.Ordinal))
            {
                int.TryParse(line.Substring("# scanObjectCount\t".Length), out _cachedZdoScanObjectCount);
                return;
            }
            if (line.StartsWith("# scanPassesCompleted\t", System.StringComparison.Ordinal))
            {
                int.TryParse(line.Substring("# scanPassesCompleted\t".Length), out _zdoBroadScanPassesCompleted);
                return;
            }
            if (line.StartsWith("# scanUpdatedUnixSeconds\t", System.StringComparison.Ordinal))
            {
                long.TryParse(line.Substring("# scanUpdatedUnixSeconds\t".Length), out _cachedZdoScanUpdatedUnixSeconds);
            }
        }

        private static long GetUnixSeconds()
        {
            return (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds;
        }

        private static string CleanPrefabName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return "unknown";
            }

            return prefabName.Replace("(Clone)", string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static bool TryClaimZdoRecord(ZDO zdo, string reason)
        {
            _claimAttempts++;

            if (zdo == null || !zdo.IsValid())
            {
                _skippedNoZdo++;
                return false;
            }

            if (!zdo.Persistent)
            {
                _skippedNotPersistent++;
                return false;
            }

            int prefabHash = zdo.GetPrefab();
            if (prefabHash == 0)
            {
                _skippedNoPrefab++;
                return false;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            if (prefab == null)
            {
                _skippedNoPrefab++;
                return false;
            }

            if (!IsEligibleStaticPiecePrefab(prefabHash, prefab))
            {
                _skippedNotPiecePrefab++;
                return false;
            }

            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (_playerBuiltPiecesOnly.Value && creator == 0L)
            {
                _skippedNoCreator++;
                return false;
            }

            long serverId = ZDOMan.GetSessionID();
            long oldOwner = zdo.GetOwner();
            if (oldOwner == serverId)
            {
                _alreadyServerOwned++;
                RecordOwnershipCache(zdo, prefab.name, creator);
                return false;
            }

            if (_dryRunStaticPieceServerOwnership.Value)
            {
                _claimed++;
                _totalClaimed++;
                LogClaim("DRY-RUN would claim", prefab.name, zdo, oldOwner, creator, reason);
                return true;
            }

            zdo.SetOwner(serverId);
            ZDOMan.instance.ForceSendZDO(zdo.m_uid);
            _claimed++;
            _totalClaimed++;
            RecordOwnershipCache(zdo, prefab.name, creator);
            LogClaim("Claimed", prefab.name, zdo, oldOwner, creator, reason);
            return true;
        }

        private static bool IsEligibleStaticPiecePrefab(int prefabHash, GameObject prefab)
        {
            bool eligible;
            if (_prefabEligibilityCache.TryGetValue(prefabHash, out eligible))
            {
                return eligible;
            }

            Piece piece = prefab.GetComponent<Piece>();
            if (piece == null)
            {
                piece = prefab.GetComponentInChildren<Piece>(true);
            }

            eligible = piece != null;
            if (eligible && _requireWearNTear.Value && prefab.GetComponent<WearNTear>() == null && prefab.GetComponentInChildren<WearNTear>(true) == null)
            {
                eligible = false;
            }
            if (eligible && IsDynamicOrPhysicsObject(prefab))
            {
                eligible = false;
            }

            _prefabEligibilityCache[prefabHash] = eligible;
            return eligible;
        }

        private static bool IsDynamicOrPhysicsObject(GameObject root)
        {
            if (root.GetComponentInChildren<Rigidbody>(true) != null)
            {
                return true;
            }
            if (root.GetComponentInChildren<Ship>(true) != null)
            {
                return true;
            }
            if (root.GetComponentInChildren<Vagon>(true) != null)
            {
                return true;
            }
            if (root.GetComponentInChildren<ItemDrop>(true) != null)
            {
                return true;
            }
            if (root.GetComponentInChildren<Character>(true) != null)
            {
                return true;
            }
            if (root.GetComponentInChildren<Floating>(true) != null)
            {
                return true;
            }

            return false;
        }

        private static bool ShouldRunExperiment()
        {
            if (_enabled == null || !_enabled.Value || _enableStaticPieceServerOwnership == null || !_enableStaticPieceServerOwnership.Value)
            {
                return false;
            }
            if (_dedicatedOnly != null && _dedicatedOnly.Value && !Application.isBatchMode)
            {
                return false;
            }
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
            {
                return false;
            }

            return true;
        }

        private static void AdvertiseServerMetadata()
        {
            if (_enabled == null || !_enabled.Value || ZNet.instance == null || !ZNet.instance.IsServer() ||
                ZNet.instance.m_serverSyncedPlayerData == null)
            {
                return;
            }
            if (_dedicatedOnly != null && _dedicatedOnly.Value && !Application.isBatchMode)
            {
                return;
            }

            // Server-synced player data is read when each peer is initialized;
            // rewriting the same values on a timer only adds work to the server
            // update loop and can create needless sync churn. Re-advertise only
            // when Valheim has replaced the live ZNet instance.
            if (_serverMetadataAdvertised && object.ReferenceEquals(_metadataAdvertisedForInstance, ZNet.instance))
            {
                return;
            }

            ZNet.instance.m_serverSyncedPlayerData[SyncedVersionKey] = PluginVersion;
            ZNet.instance.m_serverSyncedPlayerData[SyncedStaticOwnershipKey] =
                (_enableStaticPieceServerOwnership != null && _enableStaticPieceServerOwnership.Value).ToString();
            ZNet.instance.m_serverSyncedPlayerData[SyncedOwnershipCacheKey] =
                (_enableBackgroundOwnershipAudit != null && _enableBackgroundOwnershipAudit.Value &&
                 _enableOwnershipCache != null && _enableOwnershipCache.Value).ToString();
            ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainLimitsEnabledKey] =
                (_enableExtendedTerrainLimits != null && _enableExtendedTerrainLimits.Value).ToString();
            ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainRaiseLimitKey] =
                GetTerrainRaiseLimitMeters().ToString(System.Globalization.CultureInfo.InvariantCulture);
            ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainDigLimitKey] =
                GetTerrainDigLimitMeters().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!_serverMetadataAdvertised && _log != null)
            {
                _log.LogInfo("Advertised TerramizerServer companion metadata to connecting clients. version=" + PluginVersion +
                             ", staticOwnership=" + ZNet.instance.m_serverSyncedPlayerData[SyncedStaticOwnershipKey] +
                             ", ownershipCache=" + ZNet.instance.m_serverSyncedPlayerData[SyncedOwnershipCacheKey] +
                             ", extendedTerrainLimits=" + ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainLimitsEnabledKey] +
                             ", terrainRaiseLimit=" + ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainRaiseLimitKey] +
                             ", terrainDigLimit=" + ZNet.instance.m_serverSyncedPlayerData[SyncedTerrainDigLimitKey] + ".");
            }
            _serverMetadataAdvertised = true;
            _metadataAdvertisedForInstance = ZNet.instance;
        }

        private static void RegisterCompanionRpcsWhenReady()
        {
            if (ZRoutedRpc.instance == null || object.ReferenceEquals(_registeredRoutedRpcInstance, ZRoutedRpc.instance))
            {
                return;
            }

            ZRoutedRpc.instance.Register(RpcRequestCompanionMetadata, new System.Action<long>(OnRequestCompanionMetadata));
            ZRoutedRpc.instance.Register(RpcRequestCompanionMetadataV2, new System.Action<long>(OnRequestCompanionMetadataV2));
            _registeredRoutedRpcInstance = ZRoutedRpc.instance;
        }

        private static void OnRequestCompanionMetadata(long sender)
        {
            if (_enabled == null || !_enabled.Value || ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            if (_dedicatedOnly != null && _dedicatedOnly.Value && !Application.isBatchMode)
            {
                return;
            }

            bool staticOwnership = _enableStaticPieceServerOwnership != null && _enableStaticPieceServerOwnership.Value;
            bool ownershipCache = _enableBackgroundOwnershipAudit != null && _enableBackgroundOwnershipAudit.Value &&
                                  _enableOwnershipCache != null && _enableOwnershipCache.Value;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCompanionMetadata, PluginVersion, staticOwnership, ownershipCache);
            _companionRpcReplies++;

            if (!_companionRpcReplyLogged && _log != null)
            {
                _companionRpcReplyLogged = true;
                _log.LogInfo("Answered Terramizer companion metadata RPC. version=" + PluginVersion +
                             ", staticOwnership=" + staticOwnership +
                             ", ownershipCache=" + ownershipCache +
                             ".");
            }
        }

        private static void OnRequestCompanionMetadataV2(long sender)
        {
            if (_enabled == null || !_enabled.Value || ZRoutedRpc.instance == null || ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }
            if (_dedicatedOnly != null && _dedicatedOnly.Value && !Application.isBatchMode)
            {
                return;
            }

            bool staticOwnership = _enableStaticPieceServerOwnership != null && _enableStaticPieceServerOwnership.Value;
            bool ownershipCache = _enableBackgroundOwnershipAudit != null && _enableBackgroundOwnershipAudit.Value &&
                                  _enableOwnershipCache != null && _enableOwnershipCache.Value;
            bool extendedTerrain = _enableExtendedTerrainLimits != null && _enableExtendedTerrainLimits.Value;
            float terrainRaiseLimit = GetTerrainRaiseLimitMeters();
            float terrainDigLimit = GetTerrainDigLimitMeters();
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCompanionMetadataV2, PluginVersion, staticOwnership, ownershipCache, extendedTerrain, terrainRaiseLimit, terrainDigLimit);
            _companionRpcReplies++;
        }

        private static void LogOwnershipMaintenanceFailure(System.Exception ex)
        {
            if (_ownershipMaintenanceFailureLogged || _log == null)
            {
                return;
            }

            _ownershipMaintenanceFailureLogged = true;
            _log.LogWarning("Optional ownership maintenance hit an error; vanilla server simulation continues and the next bounded pass will retry: " + ex.Message);
        }

        private static void LogClaim(string action, string prefabName, ZDO zdo, long oldOwner, long creator, string reason)
        {
            if (_log == null)
            {
                return;
            }

            bool watched = IsWatchedInteractivePrefab(prefabName);
            if (watched)
            {
                _watchedClaims++;
                _totalWatchedClaims++;
            }

            if ((_logDevClaims == null || !_logDevClaims.Value) && (!watched || _logWatchedClaims == null || !_logWatchedClaims.Value))
            {
                return;
            }

            _log.LogInfo("[dev ownership] " + action + " " + prefabName +
                         " zdo=" + zdo.m_uid +
                         " oldOwner=" + oldOwner +
                         " newOwner=" + zdo.GetOwner() +
                         " creator=" + creator +
                         " sector=" + zdo.GetSector() +
                         " reason=" + reason + ".");
        }

        private static bool IsWatchedInteractivePrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return false;
            }

            string name = prefabName.ToLowerInvariant();
            return name.Contains("bed") ||
                   name.Contains("chest") ||
                   name.Contains("portal") ||
                   name.Contains("fire") ||
                   name.Contains("hearth") ||
                   name.Contains("bonfire") ||
                   name.Contains("workbench") ||
                   name.Contains("forge") ||
                   name.Contains("cauldron") ||
                   name.Contains("fermenter") ||
                   name.Contains("smelter") ||
                   name.Contains("kiln") ||
                   name.Contains("windmill") ||
                   name.Contains("spinningwheel") ||
                   name.Contains("eitrrefinery") ||
                   name.Contains("blackforge") ||
                   name.Contains("magetable") ||
                   name.Contains("artisan") ||
                   name.Contains("obliterator") ||
                   name.Contains("sign") ||
                   name.Contains("itemstand") ||
                   name.Contains("armorstand") ||
                   name.Contains("ward") ||
                   name.Contains("guardstone") ||
                   name.Contains("door") ||
                   name.Contains("gate") ||
                   name.Contains("cartography") ||
                   name.Contains("sapcollector");
        }

        private static void LogSkip(Piece piece, string reason)
        {
            if (_logDevSkips == null || !_logDevSkips.Value || _log == null || piece == null)
            {
                return;
            }

            _log.LogInfo("[dev ownership] skipped " + piece.gameObject.name + ": " + reason + ".");
        }

        private static void LogOwnershipSummary()
        {
            if (_log == null)
            {
                return;
            }

            long intervalClaims = Take(ref _claimed);
            long intervalWatchedClaims = Take(ref _watchedClaims);
            long skipped = Take(ref _skippedNoZdo) +
                           Take(ref _skippedNoPrefab) +
                           Take(ref _skippedNotPiecePrefab) +
                           Take(ref _skippedNotPersistent) +
                           Take(ref _skippedNoCreator) +
                           Take(ref _skippedNoWearNTear) +
                           Take(ref _skippedDynamic) +
                           Take(ref _skippedNotReady);

            _log.LogInfo("[dev ownership summary] claims=" + intervalClaims +
                         ", totalClaims=" + _totalClaimed +
                         ", watchedClaims=" + intervalWatchedClaims +
                         ", totalWatchedClaims=" + _totalWatchedClaims +
                         ", scanMode=" + (IsMaintenanceScanMode() ? "maintenance" : "warmup") +
                         ", scanPasses=" + _zdoBroadScanPassesCompleted +
                         ", scanInterval=" + GetOwnershipScanIntervalSeconds() +
                         ", scanRecords=" + GetZdoRecordsPerScan() +
                         ", scanClaimLimit=" + GetMaxClaimsPerScan() +
                         ", cacheRestored=" + Take(ref _cacheRestored) +
                         ", totalCacheRestored=" + _totalCacheRestored +
                         ", cacheEntries=" + _ownershipCache.Count +
                         ", cacheChecked=" + Take(ref _cacheRecordsChecked) +
                         ", cacheRemoved=" + _cacheRemoved +
                         ", companionRpcReplies=" + Take(ref _companionRpcReplies) +
                         ", zdoScanIndex=" + _zdoScanIndex +
                         ", alreadyServerOwned=" + Take(ref _alreadyServerOwned) +
                         ", skipped=" + skipped +
                         ", checked=" + Take(ref _zdoRecordsChecked) +
                         ", attempts=" + Take(ref _claimAttempts) + ".");
        }

        private static long Take(ref long value)
        {
            long result = value;
            value = 0L;
            return result;
        }

        private void RemoveRetiredSettings()
        {
            Remove("General", "ServerOnly", true);
            Remove("General", "SafetyDefaultsVersion", 0);
            Remove("Streaming", "MaxSendQueueBytes", 16384);
            Remove("Streaming", "MinFreeSendQueueBytes", 4096);
            Remove("Streaming", "PeerSendIntervalSeconds", 0.05f);
            Remove("Streaming", "ZoneStreamingBoostExtraRadius", 0);
            Remove("Streaming", "ZoneStreamingBoostIncludeDistantZdos", true);
            Remove("Streaming", "LogZoneStreamingBoosts", false);
            Remove("Streaming", "StreamingUpdateIntervalSeconds", 0.5f);
            Remove("Diagnostics", "LogDenseSyncs", true);
            Remove("Diagnostics", "DenseSyncThreshold", 1000);
            Remove("Diagnostics", "DenseSyncLogIntervalSeconds", 30f);
            Remove("Diagnostics", "PauseExtraStreamingDuringDenseSync", true);
            Remove("Diagnostics", "DenseSyncPauseSeconds", 2f);
            Remove("Diagnostics", "DiagnosticIntervalSec", 60f);
            Remove("WorldObjects", "ExtendedZoneRadius", 0);
            Remove("WorldObjects", "CreateDestroyIntervalSeconds", 0.1f);
            Remove("WorldObjects", "RemoveObjectsIntervalSeconds", 0.5f);
            Remove("WorldObjects", "EnablePredictiveZoneStreaming", true);
            Remove("WorldObjects", "PredictionLookaheadSec", 3f);
            Remove("WorldObjects", "PredictionMinVelocity", 2f);
            Remove("WorldObjects", "PredictionMaxLookaheadZones", 9);
            Remove("WorldObjects", "EnableZDOThrottling", false);
            Remove("WorldObjects", "ZDOThrottleDistance", 500f);
            Remove("WorldObjects", "EnablePlayerPriority", true);
            Remove("ServerSimulation", "PeerZoneUpdateIntervalSeconds", 0.5f);
            Remove("ServerSimulation", "EnableMultiPeerOutsideActiveArea", true);
            Remove("ServerSimulation", "EnableHeadlessVisualGuards", true);
            Remove("ServerSimulation", "EnablePeerZoneCreation", false);
            Remove("ServerSimulation", "MaxPeerZoneCreationsPerPass", 1);
            Remove("ServerSimulation", "EnableServerOwnershipForPersistentZdos", false);
            Remove("ServerSimulation", "AllowExperimentalServerOwnershipForPersistentZdos", false);
            Remove("Performance", "ActivePlayerOwnershipBudgetMs", 0.35f);
            Remove("Performance", "IdleOwnershipBudgetMs", 2.0f);
            Remove("Performance", "MaintenanceFullAuditIntervalSeconds", 1800f);
            Remove("Sleep", "PauseExtraZoneWorkDuringSleep", true);
            Remove("Sleep", "LogSleepOptimizations", true);
            Remove("BuildInteractions", "RepairStaleWearNTearRemoveOwnership", true);
            Remove("BuildInteractions", "MaxRemoveRepairDistance", 8f);
            Remove("BuildInteractions", "LogRemoveOwnershipRepairs", true);
            Remove("ItemInteractions", "RepairStaleItemDropOwnership", true);
            Remove("ItemInteractions", "LogItemDropOwnershipRepairs", false);
            Config.Save();
        }

        private void Remove<T>(string section, string key, T defaultValue)
        {
            ConfigEntry<T> retired = Config.Bind(section, key, defaultValue, "Retired for safety.");
            Config.Remove(retired.Definition);
        }
    }

    internal sealed class OwnershipCacheEntry
    {
        public string Uid;
        public int PrefabHash;
        public long Creator;
        public string PrefabName;
        public long StoredUnixSeconds;
    }

    [HarmonyPatch(typeof(ZNetView), "Awake")]
    internal static class ZNetViewAwakeStaticOwnershipPatch
    {
        private static void Postfix(ZNetView __instance)
        {
            TerramizerServerPlugin.TryClaimFromZNetView(__instance, "znetview-awake");
        }
    }
}
