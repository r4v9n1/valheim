using System;
using System.IO;
using R4V9N1.PhysicalOcean.Volumetric;
using UnityEngine;

namespace PhysicalWater
{
    internal sealed class PhysicalWaterPersistenceRuntime : MonoBehaviour
    {
        private static PhysicalWaterPersistenceRuntime Instance;
        private VolumetricFluidStateSnapshot _pending;
        private string _pendingWorldKey;
        private string _ignoredSnapshotWorldKey;
        private string _restoredSnapshotWorldKey;

        private void Awake()
        {
            Instance = this;
            ZNet.WorldSaveStarted += SaveWorldState;
            ZNet.WorldSaveFinished += LogWorldSaveFinished;
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore Phase 5 persistence runtime active; waiting for world save/load lifecycle.");
            TryLoadWorldState();
        }

        private void Update()
        {
            if (_pending == null) TryLoadWorldState();
            if (_pending != null && PhysicalWaterDevE1Runtime.Instance != null)
                TryRestore(PhysicalWaterDevE1Runtime.Instance.Streaming);
        }

        private void OnDestroy()
        {
            ZNet.WorldSaveStarted -= SaveWorldState;
            ZNet.WorldSaveFinished -= LogWorldSaveFinished;
            if (Instance == this) Instance = null;
        }

        internal static void NotifyWorldReady()
        {
            if (Instance == null) return;
            // WorldSetup denotes a new load lifecycle, including re-entering
            // the same world without restarting Valheim. Permit one restore
            // for that lifecycle; the successful marker then blocks per-frame
            // reloads until the next WorldSetup.
            Instance._restoredSnapshotWorldKey = null;
            Instance.TryLoadWorldState();
        }

        internal static void TryRestore(VolumetricStreamingDomainController streaming)
        {
            if (Instance == null || streaming == null || Instance._pending == null) return;
            try
            {
                streaming.RestorePersistedState(Instance._pending);
                PhysicalWaterPlugin.Log.LogInfo(
                    "LiquidCore Phase 5 world state restored: world=" + Instance._pendingWorldKey +
                    ", particles=" + Instance._pending.Particles.Length + ".");
                // A successful restore consumes this world's on-disk snapshot
                // for the current session. Without a completed-world marker,
                // Update reloads the same file on the next frame and restores
                // it forever, repeatedly uploading an unchanged empty/full
                // domain and collapsing live FPS after F6.
                Instance._restoredSnapshotWorldKey = Instance._pendingWorldKey;
                Instance._pending = null;
                Instance._pendingWorldKey = null;
            }
            catch (Exception ex)
            {
                // A snapshot is valid for its saved domain geometry, not for
                // every future player-centered test window. Retrying that
                // known-incompatible snapshot every frame only produces
                // log/CPU churn and can delay explicit finite-domain testing.
                bool incompatibleDomain = ex is InvalidOperationException &&
                    ex.Message.StartsWith("Snapshot/domain mismatch", StringComparison.Ordinal);
                if (incompatibleDomain)
                {
                    Instance._ignoredSnapshotWorldKey = Instance._pendingWorldKey;
                    Instance._pending = null;
                    Instance._pendingWorldKey = null;
                }
                PhysicalWaterPlugin.Log.LogWarning(
                    "LiquidCore Phase 5 snapshot was not applied; the new domain remains unchanged: " + ex.Message);
            }
        }

        private void SaveWorldState()
        {
            try
            {
                PhysicalWaterDevE1Runtime runtime = PhysicalWaterDevE1Runtime.Instance;
                if (runtime == null || runtime.Streaming == null) return;
                string path = GetSnapshotPath(out string worldKey);
                if (path == null) return;
                runtime.Streaming.CapturePersistedState().Save(path);
                _ignoredSnapshotWorldKey = null;
                PhysicalWaterPlugin.Log.LogInfo("LiquidCore Phase 5 world state saved: world=" + worldKey + ", path=" + path + ".");
            }
            catch (Exception ex)
            {
                PhysicalWaterPlugin.Log.LogError("LiquidCore Phase 5 world state save failed: " + ex);
            }
        }

        private static void LogWorldSaveFinished()
        {
            PhysicalWaterPlugin.Log.LogInfo("LiquidCore Phase 5 world save lifecycle finished.");
        }

        private void TryLoadWorldState()
        {
            try
            {
                string path = GetSnapshotPath(out string worldKey);
                if (path == null || !File.Exists(path)) return;
                if (_ignoredSnapshotWorldKey == worldKey) return;
                if (_restoredSnapshotWorldKey == worldKey) return;
                if (_pendingWorldKey == worldKey && _pending != null) return;
                _pending = VolumetricFluidStateSnapshot.Load(path);
                _pendingWorldKey = worldKey;
                PhysicalWaterPlugin.Log.LogInfo("LiquidCore Phase 5 world state discovered: world=" + worldKey + ", path=" + path + ".");
            }
            catch (Exception ex)
            {
                _pending = null;
                _pendingWorldKey = null;
                PhysicalWaterPlugin.Log.LogWarning("LiquidCore Phase 5 world state was rejected; no fluid was restored: " + ex.Message);
            }
        }

        private static string GetSnapshotPath(out string worldKey)
        {
            worldKey = null;
            World world = ZNet.GetWorldIfIsHost();
            if (world == null || string.IsNullOrEmpty(world.m_fileName)) return null;
            worldKey = world.m_uid.ToString("X16");
            string directory = Path.Combine(Application.persistentDataPath, "LiquidCore", "Worlds");
            return Path.Combine(directory, worldKey + ".pwfs");
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ZNet), "WorldSetup")]
    internal static class PhysicalWaterPersistenceWorldSetupPatch
    {
        private static void Postfix()
        {
            PhysicalWaterPersistenceRuntime.NotifyWorldReady();
        }
    }
}
