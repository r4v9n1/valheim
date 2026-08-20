using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

[assembly: AssemblyTitle(LightMyFire.LightMyFirePlugin.PluginName)]
[assembly: AssemblyVersion(LightMyFire.LightMyFirePlugin.PluginVersion)]
[assembly: AssemblyFileVersion(LightMyFire.LightMyFirePlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(LightMyFire.LightMyFirePlugin.CreatorCredit)]
[assembly: AssemblyProduct(LightMyFire.LightMyFirePlugin.PluginName)]
[assembly: AssemblyCopyright(LightMyFire.LightMyFirePlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", LightMyFire.LightMyFirePlugin.CreatorCredit)]

namespace LightMyFire
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public sealed class LightMyFirePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.lightmyfire";
        public const string PluginName = "LightMyFire";
        public const string PluginVersion = "0.5.1";
        public const string CreatorCredit = "Created by R4V9N1";

        internal const string CoalItemName = "$item_coal";
        internal const string ResinItemName = "$item_resin";
        internal const string CoalBarrelPrefabName = "R4V9N1_LightMyFireCoalBarrel";
        internal const string ResinBarrelPrefabName = "R4V9N1_LightMyFireResinBarrel";
        private const string AssetBundleResourceName = "LightMyFire.Assets.lightmyfire_assets";
        private const string CoalVisualAssetPath = "Assets/LightMyFire/Generated/Prefabs/LightMyFire_CoalBarrelVisual.prefab";
        private const string ResinVisualAssetPath = "Assets/LightMyFire/Generated/Prefabs/LightMyFire_ResinBarrelVisual.prefab";

        // Gameplay-affecting settings are marked IsAdminOnly and synced by Jotunn's
        // SynchronizationManager (ConfigSynchronizationEnabled = true below), so every client uses
        // the server's values regardless of its own local config file. Only LogTransfers (purely
        // diagnostic, client-local) is left un-synced.
        //
        // VERIFICATION NOTE: this is Jotunn's documented config-sync mechanism
        // (ConfigurationManagerAttributes.IsAdminOnly + SynchronizationManager.ConfigSynchronizationEnabled),
        // which has been stable across many Jotunn releases. This build environment does not have
        // Jotunn.dll available to confirm the exact installed version's API still matches - verify
        // against the Jotunn.dll referenced by this project (or Jotunn's docs/source for the pinned
        // ValheimModding-Jotunn-2.29.2 dependency in thunderstore/manifest.json) before shipping, and
        // confirm in a real 2-client test that a client's local .cfg values are overridden by the
        // server's.
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _feedRange;
        private static ConfigEntry<float> _refillIntervalMinutes;
        private static ConfigEntry<bool> _logTransfers;
        private static ConfigEntry<bool> _logDiagnostics;
        private static ManualLogSource _log;

        private bool _piecesRegistered;
        private float _nextRefillScan;
        private float _nextMaintenanceTick;
        private ZRoutedRpc _registeredRoutedRpcInstance;
        private AssetBundle _assetBundle;
        private GameObject _coalVisualPrefab;
        private GameObject _resinVisualPrefab;

        private void Awake()
        {
            _log = Logger;

            _enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Enable automatic coal and resin feeding. Server-controlled.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _feedRange = Config.Bind("Feeding", "Range", 50f, new ConfigDescription(
                "Maximum distance in metres from a matching LightMyFire barrel to a compatible light source. Server-controlled.",
                new AcceptableValueRange<float>(1f, 100f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _refillIntervalMinutes = Config.Bind("Feeding", "RefillIntervalMinutes", 5f, new ConfigDescription(
                "How often LightMyFire checks for compatible lights that need topping up, in minutes. Server-controlled.",
                new AcceptableValueRange<float>(0.5f, 60f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _logTransfers = Config.Bind("Diagnostics", "LogTransfers", false,
                "Log successful automatic fuel transfers. Local/diagnostic only, not synced.");
            _logDiagnostics = Config.Bind("Diagnostics", "LogDiagnostics", false,
                "Log barrel runtime initialization and refill-scan summaries. Useful when diagnosing a light that is not being fed.");

            // Public 0.5.1 release keeps the 5-minute refill default introduced during development.
            // Existing installs already have the old default persisted in the BepInEx config,
            // so migrate that exact legacy value once on upgrade. Any other custom value is preserved.
            if (Mathf.Approximately(_refillIntervalMinutes.Value, 15f))
            {
                _refillIntervalMinutes.Value = 5f;
                Config.Save();
                Logger.LogInfo("Migrated Feeding > RefillIntervalMinutes from the old 15-minute default to 5 minutes.");
            }

            // No explicit "enable sync" call needed on this Jotunn version: any ConfigEntry bound
            // with ConfigurationManagerAttributes.IsAdminOnly = true (see the Config.Bind calls
            // above) is picked up and synced automatically by Jotunn's own ZNet_Awake/RPC_PeerInfo
            // hooks. Confirmed via reflection against the actual installed Jotunn.dll - there is no
            // SynchronizationManager.ConfigSynchronizationEnabled member in this version.

            _nextRefillScan = Time.realtimeSinceStartup + 10f;
            _nextMaintenanceTick = Time.realtimeSinceStartup + 5f;

            LightMyFireHarmonyPatches.Apply(Logger);

            if (!LoadVisualAssets())
            {
                Logger.LogError("LightMyFire could not load its embedded barrel models; piece registration is disabled.");
                return;
            }

            PrefabManager.OnVanillaPrefabsAvailable += RegisterBarrels;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Logger.LogInfo("A shared timed scan tops up under-fueled coal and resin lights from matching barrels.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterBarrels;
            LightMyFireHarmonyPatches.Unpatch();
            if (_assetBundle != null)
            {
                _assetBundle.Unload(false);
                _assetBundle = null;
            }
        }

        private bool LoadVisualAssets()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(AssetBundleResourceName))
                {
                    if (stream == null)
                    {
                        Logger.LogError("Missing embedded resource '" + AssetBundleResourceName + "'.");
                        return false;
                    }

                    byte[] bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                        {
                            break;
                        }
                        offset += read;
                    }

                    if (offset != bytes.Length)
                    {
                        Logger.LogError("The embedded barrel AssetBundle could not be read completely.");
                        return false;
                    }

                    _assetBundle = AssetBundle.LoadFromMemory(bytes);
                }

                if (_assetBundle == null)
                {
                    Logger.LogError("Unity could not load the embedded barrel AssetBundle.");
                    return false;
                }

                _coalVisualPrefab = _assetBundle.LoadAsset<GameObject>(CoalVisualAssetPath);
                _resinVisualPrefab = _assetBundle.LoadAsset<GameObject>(ResinVisualAssetPath);
                if (_coalVisualPrefab == null || _resinVisualPrefab == null)
                {
                    Logger.LogError("The embedded AssetBundle is missing one or both barrel visual prefabs.");
                    return false;
                }

                Logger.LogInfo("Loaded custom coal and resin barrel models from the embedded Unity AssetBundle.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to load custom barrel models: " + ex);
                return false;
            }
        }

        private void Update()
        {
            // Register the routed grant callback lazily against the actual live ZRoutedRpc instance.
            // Valheim no longer exposes the old ZRoutedRpc.Awake method we previously patched, and
            // a new routed-RPC singleton can appear after a scene transition. Tracking the instance
            // itself is both simpler and version-resilient.
            if (ZRoutedRpc.instance != null && !object.ReferenceEquals(_registeredRoutedRpcInstance, ZRoutedRpc.instance))
            {
                ZRoutedRpc.instance.Register<long, ZDOID, ZDOID, int>(LightMyFireFeeder.RpcGrantFuel, LightMyFireFeeder.RPC_GrantFuel);
                _registeredRoutedRpcInstance = ZRoutedRpc.instance;
                Logger.LogInfo("Registered LightMyFire cross-peer fuel grant RPC.");
            }

            if (!_piecesRegistered)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;

            // Lightweight maintenance (stale transaction sweep, invalid-item safety net) runs on its
            // own short cadence, independent of the much longer refill scan, and only ever touches
            // barrels this peer owns.
            if (now >= _nextMaintenanceTick)
            {
                _nextMaintenanceTick = now + 10f;
                LightMyFireBarrel.TickMaintenance();
                LightMyFireBarrel.EjectInvalidItemsFromOwnedBarrels();
            }

            if (!IsEnabled())
            {
                return;
            }

            if (!LightMyFireBarrel.HasActiveBarrels())
            {
                if (now >= _nextRefillScan)
                {
                    float missingBarrelInterval = _refillIntervalMinutes == null ? 5f : Mathf.Clamp(_refillIntervalMinutes.Value, 0.5f, 60f);
                    _nextRefillScan = now + missingBarrelInterval * 60f;
                    LogNoActiveBarrels();
                }
                return;
            }

            if (now < _nextRefillScan)
            {
                return;
            }

            float intervalMinutes = _refillIntervalMinutes == null ? 5f : Mathf.Clamp(_refillIntervalMinutes.Value, 0.5f, 60f);
            _nextRefillScan = now + intervalMinutes * 60f;
            LightMyFireFeeder.ScanAndRequestRefills();
        }

        private void RegisterBarrels()
        {
            if (_piecesRegistered)
            {
                return;
            }

            GameObject baseBarrel = ResolveBaseBarrelPrefab();
            if (baseBarrel == null)
            {
                Logger.LogError("Could not find any current Valheim build piece that behaves as a barrel/container. LightMyFire pieces were not registered.");
                return;
            }

            bool coalRegistered = RegisterBarrel(
                CoalBarrelPrefabName,
                "LightMyFire Coal Barrel",
                "Stores coal and refills empty coal-burning lights within range.",
                CoalItemName,
                _coalVisualPrefab,
                baseBarrel);
            bool resinRegistered = RegisterBarrel(
                ResinBarrelPrefabName,
                "LightMyFire Resin Barrel",
                "Stores resin and refills empty resin-burning lights within range.",
                ResinItemName,
                _resinVisualPrefab,
                baseBarrel);

            _piecesRegistered = coalRegistered && resinRegistered;
            if (_piecesRegistered)
            {
                PrefabManager.OnVanillaPrefabsAvailable -= RegisterBarrels;
                Logger.LogInfo("Registered the coal and resin barrels in Hammer > Misc with 32 inventory slots each.");
            }
        }

        private GameObject ResolveBaseBarrelPrefab()
        {
            // Try known historical barrel names first, but never depend on one exact Valheim name.
            string[] barrelNames =
            {
                "piece_chestbarrel",
                "piece_chest_barrel",
                "piece_barrel",
                "piece_barrel_wood",
                "barrel"
            };

            for (int i = 0; i < barrelNames.Length; i++)
            {
                GameObject known = PrefabManager.Instance.GetPrefab(barrelNames[i]);
                if (IsUsableContainerPiece(known))
                {
                    Logger.LogInfo("Using Valheim barrel base prefab '" + known.name + "'.");
                    return known;
                }
            }

            // Jotunn's prefab cache is built specifically for the current game scene, so scan the
            // live cache for whatever Iron Gate currently calls its barrel container prefab.
            GameObject best = null;
            int bestScore = int.MinValue;
            var cached = PrefabManager.Cache.GetPrefabs(typeof(GameObject));
            foreach (var entry in cached)
            {
                GameObject candidate = entry.Value as GameObject;
                if (!IsUsableContainerPiece(candidate))
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(candidate.name) ? entry.Key : candidate.name;
                string lower = name.ToLowerInvariant();
                int score = 0;
                if (lower.Contains("barrel")) score += 1000;
                if (lower.StartsWith("piece_")) score += 100;
                if (lower.Contains("wood")) score += 30;
                if (lower.Contains("chest")) score += 10;
                if (lower.Contains("blackmetal")) score -= 20;
                if (lower.Contains("cart")) score -= 500;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            if (best != null && bestScore >= 1000)
            {
                Logger.LogInfo("Discovered current Valheim barrel base prefab '" + best.name + "' dynamically.");
                return best;
            }

            // Last-resort compatibility fallback: use a normal buildable container if the current
            // game has no barrel-named container at all. The custom LightMyFire decoration still
            // gives the piece its own appearance, and this is preferable to failing registration.
            string[] fallbackNames = { "piece_chest", "piece_chest_wood", "piece_chest_private" };
            for (int i = 0; i < fallbackNames.Length; i++)
            {
                GameObject fallback = PrefabManager.Instance.GetPrefab(fallbackNames[i]);
                if (IsUsableContainerPiece(fallback))
                {
                    Logger.LogWarning("No barrel-named container prefab was found; using fallback container '" + fallback.name + "'.");
                    return fallback;
                }
            }

            if (best != null)
            {
                Logger.LogWarning("No barrel-named container prefab was found; using discovered container '" + best.name + "'.");
                return best;
            }

            return null;
        }

        private static bool IsUsableContainerPiece(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            // Current Valheim container pieces rely on the full native prefab graph, not merely a
            // Piece + Container pair. In particular, Container and WearNTear expect the cloned
            // ZNetView/soft-reference dependencies to remain intact when their Awake methods run.
            return prefab.GetComponent<Piece>() != null &&
                   prefab.GetComponent<Container>() != null &&
                   prefab.GetComponent<WearNTear>() != null &&
                   prefab.GetComponent<ZNetView>() != null &&
                   prefab.GetComponentsInChildren<Collider>(true).Length > 0;
        }

        private bool RegisterBarrel(string prefabName, string displayName, string description, string fuelItemName, GameObject visualPrefab, GameObject baseBarrel)
        {
            if (PieceManager.Instance.GetPiece(prefabName) != null)
            {
                return true;
            }

            PieceConfig config = new PieceConfig
            {
                Name = displayName,
                Description = description,
                PieceTable = "Hammer",
                Category = "Misc",
                CraftingStation = "Workbench"
            };
            config.AddRequirement("Wood", 100, true);
            config.AddRequirement("Iron", 40, true);
            config.AddRequirement("Tar", 40, true);

            // IMPORTANT: clone through Jotunn, never raw Unity Object.Instantiate. Valheim 0.221.x
            // moved more prefab dependencies into its runtime/soft-reference asset system. Jotunn's
            // vanilla-copy CustomPiece constructor routes through PrefabManager.CreateClonedPrefab
            // and AssetManager.ClonePrefab, preserving those dependencies. A raw Instantiate looked
            // visually correct but produced null references inside Container.Awake and WearNTear.Awake
            // when the placement ghost or placed piece was instantiated.
            CustomPiece customPiece = new CustomPiece(prefabName, baseBarrel.name, config);
            GameObject prefab = customPiece.PiecePrefab;
            if (prefab == null)
            {
                Logger.LogError("Jotunn could not clone custom piece '" + prefabName + "' from base '" + baseBarrel.name + "'.");
                return false;
            }

            string validationError;
            if (!ValidateNativeBarrelClone(prefab, out validationError))
            {
                Logger.LogError("Native barrel clone validation failed for '" + prefabName + "': " + validationError + ". Piece registration was cancelled.");
                UnityEngine.Object.Destroy(prefab);
                return false;
            }

            Container container = prefab.GetComponent<Container>();

            container.m_name = displayName;
            container.m_width = 8;
            container.m_height = 4;
            container.m_autoDestroyEmpty = false;

            LightMyFireBarrel barrel = prefab.GetComponent<LightMyFireBarrel>();
            if (barrel == null)
            {
                barrel = prefab.AddComponent<LightMyFireBarrel>();
            }
            barrel.Configure(fuelItemName);

            if (!AttachNativeRangeMarker(prefab))
            {
                Logger.LogWarning("Could not attach LightMyFire dotted feeding-radius ring to '" + displayName + "'. The barrel will still function, but its feeding-radius ring will be unavailable.");
            }

            if (!AttachBarrelDecoration(prefab, visualPrefab))
            {
                Logger.LogError("Could not attach the barrel decoration for '" + displayName + "'; registration was cancelled.");
                UnityEngine.Object.Destroy(prefab);
                return false;
            }

            PieceManager.Instance.AddPiece(customPiece);
            Logger.LogInfo("Prepared '" + displayName + "' as a Jotunn native clone of '" + baseBarrel.name +
                "' with " + prefab.GetComponentsInChildren<Collider>(true).Length +
                " native collider(s); custom lid/plaque are visual-only.");
            return true;
        }

        private static bool AttachNativeRangeMarker(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            try
            {
                // Use Valheim's workbench range-marker material so the custom ring belongs visually
                // in Valheim, but do NOT clone the workbench area geometry. LightMyFire draws only
                // discrete white dashes on the circumference at the exact configured feed radius.
                Material markerMaterial = null;
                GameObject workbench = PrefabManager.Instance.GetPrefab("piece_workbench");
                if (workbench != null)
                {
                    CraftingStation station = workbench.GetComponent<CraftingStation>();
                    if (station != null)
                    {
                        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                        FieldInfo markerField = typeof(CraftingStation).GetField("m_areaMarker", flags);
                        GameObject sourceMarker = markerField == null ? null : markerField.GetValue(station) as GameObject;
                        if (sourceMarker != null)
                        {
                            Renderer sourceRenderer = sourceMarker.GetComponentInChildren<Renderer>(true);
                            if (sourceRenderer != null && sourceRenderer.sharedMaterial != null)
                            {
                                markerMaterial = new Material(sourceRenderer.sharedMaterial);
                                markerMaterial.name = "LightMyFire_DottedRangeMaterial";
                                if (markerMaterial.HasProperty("_Color"))
                                {
                                    markerMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.92f));
                                }
                                if (markerMaterial.HasProperty("_BaseColor"))
                                {
                                    markerMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.92f));
                                }
                                markerMaterial.renderQueue = 3100;
                                if (markerMaterial.HasProperty("_ZWrite")) markerMaterial.SetInt("_ZWrite", 0);
                            }
                        }
                    }
                }

                if (markerMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        shader = Shader.Find("Unlit/Transparent");
                    }
                    if (shader == null)
                    {
                        return false;
                    }

                    markerMaterial = new Material(shader);
                    markerMaterial.name = "LightMyFire_DottedRangeMaterial";
                    markerMaterial.color = new Color(1f, 1f, 1f, 0.92f);
                    markerMaterial.renderQueue = 3100;
                }

                GameObject marker = new GameObject("R4V9N1_LightMyFire_RangeMarker");
                marker.transform.SetParent(prefab.transform, false);
                marker.transform.localPosition = Vector3.zero;
                marker.transform.localRotation = Quaternion.identity;
                marker.transform.localScale = Vector3.one;

                marker.AddComponent<MeshFilter>();
                MeshRenderer renderer = marker.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = markerMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                marker.SetActive(false);

                LightMyFireRangeMarker controller = prefab.GetComponent<LightMyFireRangeMarker>();
                if (controller == null)
                {
                    controller = prefab.AddComponent<LightMyFireRangeMarker>();
                }
                controller.Configure(marker);
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.LogWarning("Failed to prepare dotted feeding-radius ring: " + ex.Message);
                }
                return false;
            }
        }

        private static bool AttachBarrelDecoration(GameObject prefab, GameObject visualPrefab)
        {
            if (prefab == null || visualPrefab == null)
            {
                return false;
            }

            // Fit cosmetic parts against the physical barrel volume. The previous pass combined every
            // renderer in the prefab, including inactive/auxiliary renderers, which can produce a huge
            // or offset bounds box and is why the lid/plaque could float away from the actual barrel.
            Bounds barrelBounds;
            if (!TryGetPhysicalLocalBounds(prefab.transform, out barrelBounds))
            {
                Renderer[] activeRenderers = prefab.GetComponentsInChildren<Renderer>(false);
                if (activeRenderers.Length == 0)
                {
                    return false;
                }
                barrelBounds = GetCombinedLocalBounds(prefab.transform, activeRenderers);
            }

            float diameter = Mathf.Min(barrelBounds.size.x, barrelBounds.size.z);
            if (diameter <= 0.05f || float.IsNaN(diameter) || float.IsInfinity(diameter))
            {
                return false;
            }

            GameObject visual = UnityEngine.Object.Instantiate(visualPrefab, prefab.transform, false);
            visual.name = "LightMyFireDecoration";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            Renderer[] visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
            if (visualRenderers.Length == 0)
            {
                UnityEngine.Object.Destroy(visual);
                return false;
            }

            // Decoration is visual-only. Never let imported art change placement, clicking or damage.
            Collider[] decorationColliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < decorationColliders.Length; i++)
            {
                UnityEngine.Object.Destroy(decorationColliders[i]);
            }

            Transform lid = visual.transform.Find("MetalLid");
            Transform plaque = visual.transform.Find("FrontPlaque");
            if (lid == null || plaque == null)
            {
                UnityEngine.Object.Destroy(visual);
                return false;
            }

            // Authored lid diameter = 0.95 m. Keep it slightly inside the physical barrel rim and
            // place the model's bottom at the barrel top. Coordinates are expressed directly in the
            // native piece root's local space, avoiding the old double-centre/child-offset scheme.
            float lidScale = (diameter * 0.90f) / 0.95f;
            lid.localScale = Vector3.one * lidScale;
            lid.localPosition = new Vector3(
                barrelBounds.center.x,
                barrelBounds.max.y + Mathf.Max(0.006f, diameter * 0.008f),
                barrelBounds.center.z);
            lid.localRotation = Quaternion.identity;

            // Authored plaque width = 0.56 m. Mirror X once here because the authored plaque texture is
            // reversed when viewed from the barrel front. The geometry is symmetric, so this fixes
            // COAL/RESIN text without changing placement or interaction. Place it outside local -Z.
            float plaqueScale = (diameter * 0.42f) / 0.56f;
            plaque.localScale = new Vector3(-plaqueScale, plaqueScale, plaqueScale);
            plaque.localPosition = new Vector3(
                barrelBounds.center.x,
                barrelBounds.center.y + barrelBounds.size.y * 0.06f,
                barrelBounds.min.z - Mathf.Max(0.008f, diameter * 0.012f));
            plaque.localRotation = Quaternion.identity;

            return true;
        }

        private static bool ValidateNativeBarrelClone(GameObject prefab, out string error)
        {
            error = null;
            if (prefab.GetComponent<Piece>() == null) { error = "missing Piece"; return false; }
            if (prefab.GetComponent<ZNetView>() == null) { error = "missing ZNetView"; return false; }
            if (prefab.GetComponent<Container>() == null) { error = "missing Container"; return false; }
            if (prefab.GetComponent<WearNTear>() == null) { error = "missing WearNTear"; return false; }
            if (prefab.GetComponentsInChildren<Collider>(true).Length == 0) { error = "missing native collider hierarchy"; return false; }
            return true;
        }

        private static bool TryGetPhysicalLocalBounds(Transform root, out Bounds bounds)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            bool initialized = false;
            bounds = new Bounds(Vector3.zero, Vector3.zero);

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                Bounds colliderLocal;
                if (TryGetColliderLocalBounds(collider, out colliderLocal))
                {
                    EncapsulateLocalBounds(root, collider.transform, colliderLocal, ref bounds, ref initialized);
                    continue;
                }

                // Fallback for uncommon Collider subclasses. Unity's world-space bounds can be empty
                // for disabled/inactive prefab colliders, which is why the explicit local-shape path
                // above is preferred for Box/Sphere/Capsule/Mesh colliders.
                Bounds world = collider.bounds;
                if (world.size.sqrMagnitude > 0.000001f)
                {
                    EncapsulateWorldBounds(root, world, ref bounds, ref initialized);
                }
            }

            return initialized;
        }

        private static bool TryGetColliderLocalBounds(Collider collider, out Bounds bounds)
        {
            BoxCollider box = collider as BoxCollider;
            if (box != null)
            {
                bounds = new Bounds(box.center, box.size);
                return box.size.sqrMagnitude > 0.000001f;
            }

            SphereCollider sphere = collider as SphereCollider;
            if (sphere != null)
            {
                float diameter = sphere.radius * 2f;
                bounds = new Bounds(sphere.center, Vector3.one * diameter);
                return diameter > 0.0001f;
            }

            CapsuleCollider capsule = collider as CapsuleCollider;
            if (capsule != null)
            {
                float diameter = capsule.radius * 2f;
                Vector3 size = Vector3.one * diameter;
                float axisLength = Mathf.Max(capsule.height, diameter);
                if (capsule.direction == 0) size.x = axisLength;
                else if (capsule.direction == 1) size.y = axisLength;
                else size.z = axisLength;
                bounds = new Bounds(capsule.center, size);
                return size.sqrMagnitude > 0.000001f;
            }

            MeshCollider mesh = collider as MeshCollider;
            if (mesh != null && mesh.sharedMesh != null)
            {
                bounds = mesh.sharedMesh.bounds;
                return bounds.size.sqrMagnitude > 0.000001f;
            }

            bounds = new Bounds(Vector3.zero, Vector3.zero);
            return false;
        }

        private static void EncapsulateLocalBounds(Transform root, Transform source, Bounds localBounds, ref Bounds result, ref bool initialized)
        {
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 rootLocal = root.InverseTransformPoint(source.TransformPoint(corner));
                if (!initialized)
                {
                    result = new Bounds(rootLocal, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(rootLocal);
                }
            }
        }

        private static void EncapsulateWorldBounds(Transform root, Bounds world, ref Bounds result, ref bool initialized)
        {
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(corner);
                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }

        private static Bounds GetCombinedLocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = new Bounds(Vector3.zero, Vector3.zero);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }
                EncapsulateWorldBounds(root, renderers[i].bounds, ref result, ref initialized);
            }

            return result;
        }


        internal static bool IsDiagnosticsEnabled()
        {
            return _logDiagnostics != null && _logDiagnostics.Value;
        }

        internal static void LogNoActiveBarrels()
        {
            if (!IsDiagnosticsEnabled() || _log == null)
            {
                return;
            }

            _log.LogWarning("Refill scan skipped: no runtime-ready LightMyFire barrels are registered on this peer.");
        }

        internal static void LogBarrelRuntimeReady(string objectName, string fuelItemName, int fuelCount)
        {
            if (!IsDiagnosticsEnabled() || _log == null)
            {
                return;
            }

            string fuel = string.Equals(fuelItemName, ResinItemName, StringComparison.Ordinal) ? "resin" : "coal";
            _log.LogInfo("Runtime-ready " + fuel + " barrel '" + objectName + "' with " + fuelCount + " fuel item(s).");
        }

        internal static void LogScanSummary(int fireplacesFound, int ownedFireplaces, int compatibleFireplaces, int underFueledFireplaces, int inRangeMatches, int requestsSent)
        {
            if (!IsDiagnosticsEnabled() || _log == null)
            {
                return;
            }

            _log.LogInfo("Refill scan: fireplaces=" + fireplacesFound +
                ", owned=" + ownedFireplaces +
                ", compatible=" + compatibleFireplaces +
                ", needFuel=" + underFueledFireplaces +
                ", inRange=" + inRangeMatches +
                ", requests=" + requestsSent + ".");
        }

        internal static bool IsEnabled()
        {
            return _enabled != null && _enabled.Value;
        }

        internal static float GetFeedRange()
        {
            return _feedRange == null ? 50f : Mathf.Clamp(_feedRange.Value, 1f, 100f);
        }

        internal static void LogTransfer(int amount, string fuelItemName, Fireplace fireplace)
        {
            if (_logTransfers == null || !_logTransfers.Value || _log == null || fireplace == null || amount <= 0)
            {
                return;
            }

            string fuel = fuelItemName == ResinItemName ? "resin" : "coal";
            _log.LogInfo("Topped up " + fireplace.GetHoverName() + " with " + amount + " " + fuel + ".");
        }

        internal static void LogInvalidItemEjection(int count, string barrelObjectName)
        {
            if (_log == null || count <= 0)
            {
                return;
            }

            _log.LogWarning("Ejected " + count + " item stack(s) that did not match the configured fuel type from '" + barrelObjectName + "'.");
        }

        internal static void LogRejectedInsertion(string expectedFuelName, string rejectedItemName, string entryPoint)
        {
            if (!IsDiagnosticsEnabled() || _log == null)
            {
                return;
            }

            string expected = string.Equals(expectedFuelName, ResinItemName, StringComparison.Ordinal) ? "Resin" : "Coal";
            string rejected = string.IsNullOrEmpty(rejectedItemName) ? "unknown item" : rejectedItemName;
            _log.LogInfo("Rejected '" + rejected + "' from " + expected + " barrel via " + entryPoint + ".");
        }

        internal static string GetSupportedFuelItemName(Fireplace fireplace)
        {
            if (fireplace == null || fireplace.m_infiniteFuel || fireplace.m_maxFuel <= 0f ||
                fireplace.m_fuelItem == null || fireplace.m_fuelItem.m_itemData == null || fireplace.m_fuelItem.m_itemData.m_shared == null)
            {
                return null;
            }

            string fuelItemName = fireplace.m_fuelItem.m_itemData.m_shared.m_name;
            return string.Equals(fuelItemName, CoalItemName, StringComparison.Ordinal) ||
                string.Equals(fuelItemName, ResinItemName, StringComparison.Ordinal)
                ? fuelItemName
                : null;
        }

        internal static bool IsCompatibleFuelLight(Fireplace fireplace, string fuelItemName)
        {
            string actualFuel = GetSupportedFuelItemName(fireplace);
            return actualFuel != null && string.Equals(actualFuel, fuelItemName, StringComparison.Ordinal);
        }
    }
}
