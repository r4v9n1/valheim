using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace HereComesTheVein
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class HereComesTheVeinPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.r4v9n1.herecomesthevein";
        public const string PluginName = "HereComesTheVein";
        public const string PluginVersion = "0.1.5";

        internal const int IronOreSharePercent = 40;

        internal static ManualLogSource ModLog;
        private static GameObject _ironOrePrefab;
        private static Texture2D _ironTexture;
        private static bool _warnedMissingIronOre;
        private Harmony _harmony;

        private void Awake()
        {
            ModLog = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(HereComesTheVeinPlugin).Assembly);

            Logger.LogInfo(
                PluginName + " " + PluginVersion +
                " loaded. Copper veins now use weighted CopperOre/IronOre drops " +
                "(60/40); runtime=" + (Application.isBatchMode ? "dedicated-server" : "client") + ".");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
            }
        }

        internal static void TryConvertCopperVein(GameObject instance)
        {
            if (instance == null || !IsCopperVein(instance.name))
            {
                return;
            }

            // Loot is substituted at DropTable.GetDropList, after Valheim has
            // selected the final entries. This hook remains intentionally
            // side-effect free so prefab activation cannot double-augment loot.
        }

        private static bool IsCopperVein(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            return objectName.StartsWith("rock4_copper", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIronVeinPosition(Vector3 position)
        {
            int x = Mathf.RoundToInt(position.x);
            int z = Mathf.RoundToInt(position.z);

            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 16;
                h *= 0x7feb352du;
                h ^= h >> 15;
                h *= 0x846ca68bu;
                h ^= h >> 16;

                return true;
            }
        }

        private static void ApplyHoverName(GameObject instance)
        {
            Type hoverTextType = AccessTools.TypeByName("HoverText");
            if (hoverTextType == null)
            {
                return;
            }

            Component hover = instance.GetComponent(hoverTextType);
            if (hover == null)
            {
                return;
            }

            FieldInfo textField = AccessTools.Field(hoverTextType, "m_text");
            if (textField != null)
            {
                textField.SetValue(hover, "Iron Vein");
            }
        }

        private static void ReplaceCopperDrops(GameObject instance)
        {
            GameObject ironOre = ResolveItemPrefab("IronOre") ?? ResolveItemPrefab("IronScrap");
            if (ironOre == null)
            {
                if (!_warnedMissingIronOre)
                {
                    _warnedMissingIronOre = true;
                    ModLog?.LogWarning(
                        "Could not resolve IronOre or IronScrap from ObjectDB/ZNetScene. " +
                        "The vein will be recolored but drops were left unchanged.");
                }
                return;
            }

            ReplaceCopperEntriesOnComponent(instance, "MineRock5", ironOre);
            ReplaceCopperEntriesOnComponent(instance, "MineRock", ironOre);
        }

        private static void ReplaceCopperEntriesOnComponent(
            GameObject instance,
            string componentTypeName,
            GameObject ironOre)
        {
            Type componentType = AccessTools.TypeByName(componentTypeName);
            if (componentType == null)
            {
                return;
            }

            Component component = instance.GetComponent(componentType);
            if (component == null)
            {
                return;
            }

            FieldInfo dropItemsField = AccessTools.Field(componentType, "m_dropItems");
            object dropTable = dropItemsField?.GetValue(component);
            if (dropTable == null)
            {
                return;
            }

            FieldInfo dropsField = AccessTools.Field(dropTable.GetType(), "m_drops");
            IList drops = dropsField?.GetValue(dropTable) as IList;
            if (drops == null)
            {
                return;
            }

            bool alreadyAugmented = drops.Cast<object>().Any(entry =>
            {
                FieldInfo itemField = entry == null ? null : AccessTools.Field(entry.GetType(), "m_item");
                return IsIronOre(itemField?.GetValue(entry) as GameObject);
            });
            if (alreadyAugmented)
            {
                return;
            }

            int originalCount = drops.Count;
            for (int i = 0; i < originalCount; i++)
            {
                object entry = drops[i];
                if (entry == null)
                {
                    continue;
                }

                FieldInfo itemField = AccessTools.Field(entry.GetType(), "m_item");
                GameObject currentItem = itemField?.GetValue(entry) as GameObject;
                if (!IsCopperOre(currentItem))
                {
                    continue;
                }

                object ironEntry = Activator.CreateInstance(entry.GetType());
                foreach (FieldInfo field in entry.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    field.SetValue(ironEntry, field.GetValue(entry));
                }

                itemField.SetValue(ironEntry, ironOre);
                FieldInfo weightField = AccessTools.Field(entry.GetType(), "m_weight");
                if (weightField != null && weightField.FieldType == typeof(float))
                {
                    float copperWeight = (float)weightField.GetValue(entry);
                    weightField.SetValue(ironEntry, copperWeight * (2f / 3f));
                }

                drops.Add(ironEntry);
            }
        }

        private static bool IsCopperOre(GameObject item)
        {
            if (item == null)
            {
                return false;
            }

            string itemName = item.name ?? string.Empty;
            if (itemName.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                itemName = itemName.Substring(0, itemName.Length - "(Clone)".Length);
            }

            return string.Equals(itemName, "CopperOre", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIronOre(GameObject item)
        {
            if (item == null)
            {
                return false;
            }

            string itemName = item.name ?? string.Empty;
            if (itemName.EndsWith("(Clone)", StringComparison.Ordinal))
            {
                itemName = itemName.Substring(0, itemName.Length - "(Clone)".Length);
            }

            return string.Equals(itemName, "IronOre", StringComparison.OrdinalIgnoreCase);
        }

        private static GameObject ResolveItemPrefab(string prefabName)
        {
            if (string.Equals(prefabName, "IronOre", StringComparison.Ordinal) &&
                _ironOrePrefab != null)
            {
                return _ironOrePrefab;
            }

            GameObject result = ResolveFromObjectDb(prefabName) ?? ResolveFromZNetScene(prefabName);

            if (result != null && string.Equals(prefabName, "IronOre", StringComparison.Ordinal))
            {
                _ironOrePrefab = result;
            }

            return result;
        }

        private static GameObject ResolveFromObjectDb(string prefabName)
        {
            try
            {
                Type objectDbType = AccessTools.TypeByName("ObjectDB");
                if (objectDbType == null)
                {
                    return null;
                }

                object objectDb =
                    AccessTools.Property(objectDbType, "instance")?.GetValue(null, null) ??
                    AccessTools.Field(objectDbType, "instance")?.GetValue(null);

                if (objectDb == null)
                {
                    return null;
                }

                MethodInfo getItemPrefab =
                    AccessTools.Method(objectDbType, "GetItemPrefab", new[] { typeof(string) });

                return getItemPrefab?.Invoke(objectDb, new object[] { prefabName }) as GameObject;
            }
            catch (Exception ex)
            {
                ModLog?.LogDebug("ObjectDB lookup failed for " + prefabName + ": " + ex.Message);
                return null;
            }
        }

        private static GameObject ResolveFromZNetScene(string prefabName)
        {
            try
            {
                Type znetSceneType = AccessTools.TypeByName("ZNetScene");
                if (znetSceneType == null)
                {
                    return null;
                }

                object scene =
                    AccessTools.Property(znetSceneType, "instance")?.GetValue(null, null) ??
                    AccessTools.Field(znetSceneType, "instance")?.GetValue(null);

                if (scene == null)
                {
                    return null;
                }

                MethodInfo getPrefab =
                    AccessTools.Method(znetSceneType, "GetPrefab", new[] { typeof(string) });

                return getPrefab?.Invoke(scene, new object[] { prefabName }) as GameObject;
            }
            catch (Exception ex)
            {
                ModLog?.LogDebug("ZNetScene lookup failed for " + prefabName + ": " + ex.Message);
                return null;
            }
        }

        private static void ApplyIronVisual(GameObject instance)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            Texture2D ironTexture = GetIronTexture();
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    if (ironTexture != null)
                    {
                        if (material.HasProperty("_MainTex"))
                        {
                            material.SetTexture("_MainTex", ironTexture);
                            material.SetTextureScale("_MainTex", new Vector2(2f, 2f));
                        }

                        if (material.HasProperty("_BaseMap"))
                        {
                            material.SetTexture("_BaseMap", ironTexture);
                            material.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
                        }
                    }

                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", Color.white);
                    }

                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", Color.white);
                    }

                    if (material.HasProperty("_Metallic"))
                    {
                        material.SetFloat("_Metallic", 0.72f);
                    }

                    if (material.HasProperty("_Glossiness"))
                    {
                        material.SetFloat("_Glossiness", 0.28f);
                    }

                    if (material.HasProperty("_Smoothness"))
                    {
                        material.SetFloat("_Smoothness", 0.28f);
                    }

                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", new Color(0.02f, 0.025f, 0.03f, 1f));
                    }
                }
            }
        }

        private static Texture2D GetIronTexture()
        {
            if (_ironTexture != null)
            {
                return _ironTexture;
            }

            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "HereComesTheVein_BlackSilver";
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float broad = Mathf.PerlinNoise(x * 0.045f, y * 0.045f);
                    float fine = Mathf.PerlinNoise(17.3f + x * 0.13f, 31.7f + y * 0.13f);
                    float streak = Mathf.Abs(
                        Mathf.Sin((x * 0.11f) + (y * 0.065f) + broad * 4.5f));

                    bool silver = broad > 0.56f && streak > 0.72f;
                    float value = silver
                        ? Mathf.Lerp(0.48f, 0.82f, fine)
                        : Mathf.Lerp(0.055f, 0.19f, broad * 0.75f + fine * 0.25f);

                    Color pixel = new Color(
                        value * 0.96f,
                        value,
                        Mathf.Min(1f, value * 1.06f),
                        1f);

                    texture.SetPixel(x, y, pixel);
                }
            }

            texture.Apply(true, false);
            _ironTexture = texture;
            return _ironTexture;
        }
    }

    [HarmonyPatch]
    internal static class CopperDropListPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type dropTableType = AccessTools.TypeByName("DropTable");
            return dropTableType == null
                ? Enumerable.Empty<MethodBase>()
                : dropTableType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name == "GetDropList" &&
                                     typeof(List<GameObject>).IsAssignableFrom(method.ReturnType));
        }

        [HarmonyPostfix]
        private static void Postfix(List<GameObject> __result)
        {
            if (__result == null || __result.Count == 0 || !ElderHasBeenDefeated())
            {
                return;
            }

            GameObject ironOre = ResolveIronOre();
            if (ironOre == null)
            {
                return;
            }

            int replacements = 0;
            for (int i = 0; i < __result.Count; i++)
            {
                GameObject item = __result[i];
                if (item != null && IsCopperOreName(item.name) && UnityEngine.Random.value < 0.4f)
                {
                    __result[i] = ironOre;
                    replacements++;
                }
            }

            if (replacements > 0)
            {
                HereComesTheVeinPlugin.ModLog?.LogDebug("Converted " + replacements + " CopperOre drop(s) to IronOre.");
            }
        }

        private static bool ElderHasBeenDefeated()
        {
            Type zoneSystemType = AccessTools.TypeByName("ZoneSystem");
            object zoneSystem = AccessTools.Property(zoneSystemType, "instance")?.GetValue(null, null);
            if (zoneSystem == null)
            {
                return false;
            }

            Type globalKeysType = AccessTools.TypeByName("GlobalKeys");
            if (globalKeysType == null)
            {
                return false;
            }

            object elderKey;
            try
            {
                elderKey = Enum.Parse(globalKeysType, "defeated_gd_king");
            }
            catch
            {
                return false;
            }

            MethodInfo getGlobalKey = AccessTools.Method(zoneSystemType, "GetGlobalKey", new[] { globalKeysType });
            return getGlobalKey != null && (bool)getGlobalKey.Invoke(zoneSystem, new[] { elderKey });
        }

        private static GameObject ResolveIronOre()
        {
            Type objectDbType = AccessTools.TypeByName("ObjectDB");
            object objectDb = AccessTools.Property(objectDbType, "instance")?.GetValue(null, null);
            MethodInfo getItemPrefab = AccessTools.Method(objectDbType, "GetItemPrefab", new[] { typeof(string) });
            return getItemPrefab?.Invoke(objectDb, new object[] { "IronOre" }) as GameObject;
        }

        private static bool IsCopperOreName(string itemName)
        {
            return string.Equals(itemName?.Replace("(Clone)", ""), "CopperOre", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch]
    internal static class ZNetSceneCreateObjectPatch
    {
        private static MethodBase TargetMethod()
        {
            Type znetSceneType = AccessTools.TypeByName("ZNetScene");
            if (znetSceneType == null)
            {
                throw new MissingMemberException("ZNetScene type was not found.");
            }

            MethodInfo[] createObjectMethods = znetSceneType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method =>
                    method.Name == "CreateObject" &&
                    typeof(GameObject).IsAssignableFrom(method.ReturnType))
                .ToArray();

            MethodInfo exact = createObjectMethods.FirstOrDefault(method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 &&
                       string.Equals(parameters[0].ParameterType.Name, "ZDO", StringComparison.Ordinal);
            });

            MethodInfo target = exact ?? createObjectMethods.FirstOrDefault();
            if (target == null)
            {
                throw new MissingMethodException(
                    "Could not find a ZNetScene.CreateObject method returning GameObject.");
            }

            return target;
        }

        [HarmonyPostfix]
        private static void Postfix(GameObject __result)
        {
            HereComesTheVeinPlugin.TryConvertCopperVein(__result);
        }
    }

    [HarmonyPatch(typeof(MineRock5), "Awake")]
    internal static class MineRock5ActivationPatch
    {
        private static void Postfix(MineRock5 __instance)
        {
            if (__instance != null)
            {
                HereComesTheVeinPlugin.TryConvertCopperVein(__instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(MineRock5), "Damage")]
    internal static class MineRock5DamagePatch
    {
        private static void Prefix(MineRock5 __instance)
        {
            if (__instance != null)
            {
                HereComesTheVeinPlugin.TryConvertCopperVein(__instance.gameObject);
            }
        }
    }

    [HarmonyPatch(typeof(MineRock), "Damage")]
    internal static class MineRockDamagePatch
    {
        private static void Prefix(MineRock __instance)
        {
            if (__instance != null)
            {
                HereComesTheVeinPlugin.TryConvertCopperVein(__instance.gameObject);
            }
        }
    }
}
