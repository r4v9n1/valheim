using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

[assembly: AssemblyTitle(BrennivinProtection.BrennivinPlugin.PluginName)]
[assembly: AssemblyVersion(BrennivinProtection.BrennivinPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(BrennivinProtection.BrennivinPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(BrennivinProtection.BrennivinPlugin.CreatorCredit)]
[assembly: AssemblyProduct(BrennivinProtection.BrennivinPlugin.PluginName)]
[assembly: AssemblyCopyright(BrennivinProtection.BrennivinPlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", BrennivinProtection.BrennivinPlugin.CreatorCredit)]

namespace BrennivinProtection
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("r4v9n1.equipmentsheet", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    public sealed class BrennivinPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.brennivin";
        public const string PluginName = "BrennivinProtection";
        public const string PluginVersion = "1.0.1";
        public const string CreatorCredit = "Created by R4V9N1";

        internal const string PrefabName = "R4V9N1_Brennivin";
        internal const string BasePrefabName = "R4V9N1_BrennivinBase";
        internal const string DisplayName = "Brenniv\u00edn";
        internal const string BaseDisplayName = "Mead base: Brenniv\u00edn";
        internal const string ItemDescription = "One sip of this can make you forget everything....\nWhile in inventory you will not loose your items on death, if you drink it.....";

        private static readonly string[] MeadBaseCandidates =
        {
            "MeadHealthMinor",
            "MeadTasty",
            "MeadStaminaMinor",
            "MeadHealthMedium",
            "MeadEitrMinor",
            "MeadPoisonResist",
            "MeadFrostResist",
            "MeadHealthLingering"
        };

        private static readonly string[] MeadBaseBaseCandidates =
        {
            "MeadBaseHealthMinor",
            "MeadBaseTasty",
            "MeadBaseStaminaMinor",
            "MeadBaseHealthMedium",
            "MeadBaseEitrMinor",
            "MeadBasePoisonResist",
            "MeadBaseFrostResist",
            "BarleyWineBase"
        };

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _deathProtection;
        private static ConfigEntry<bool> _bedTeleport;
        private static ConfigEntry<bool> _logDiagnostics;
        private static ManualLogSource _log;

        private Harmony _harmony;
        private bool _itemRegistered;

        private void Awake()
        {
            _log = Logger;

            _enabled = Config.Bind("General", "Enabled", true, new ConfigDescription(
                "Enable Brennivin item behavior. Server-controlled.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _deathProtection = Config.Bind("General", "DeathProtection", true, new ConfigDescription(
                "If true, carrying Brennivin lets the player die normally but keep inventory and equipment, consuming one flask. Server-controlled.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _bedTeleport = Config.Bind("General", "BedTeleportOnDrink", true, new ConfigDescription(
                "If true, drinking Brennivin teleports the player to their claimed bed. Server-controlled.",
                null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            _logDiagnostics = Config.Bind("Diagnostics", "LogDiagnostics", false,
                "Log Brennivin death protection, item registration, and teleport decisions.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            PrefabManager.OnVanillaPrefabsAvailable += RegisterBrennivin;

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private void OnDestroy()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterBrennivin;
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private void RegisterBrennivin()
        {
            if (_itemRegistered || (ItemManager.Instance.GetItem(PrefabName) != null && ItemManager.Instance.GetItem(BasePrefabName) != null))
            {
                _itemRegistered = true;
                return;
            }

            GameObject baseMead = ResolveBaseMeadPrefab();
            GameObject baseMeadBase = ResolveBaseMeadBasePrefab();
            if (baseMead == null || baseMeadBase == null)
            {
                Logger.LogError("Could not find vanilla mead flask/base prefabs to clone. Brennivin was not registered.");
                return;
            }

            ItemConfig flaskConfig = new ItemConfig
            {
                Name = DisplayName,
                Description = ItemDescription,
                StackSize = 10,
                Weight = 1f
            };

            ItemConfig baseConfig = new ItemConfig
            {
                Name = BaseDisplayName,
                Description = "Ferment this into " + DisplayName + ".",
                Amount = 1,
                CraftingStation = "MeadKetill",
                MinStationLevel = 1,
                StackSize = 10,
                Weight = 1f
            };
            baseConfig.AddRequirement("Cloudberry", 50);
            baseConfig.AddRequirement("Honey", 50);
            baseConfig.AddRequirement("Thistle", 50);
            baseConfig.AddRequirement("Dandelion", 50);
            baseConfig.AddRequirement("BoneFragments", 50);

            CustomItem flask = new CustomItem(PrefabName, baseMead.name, flaskConfig);
            CustomItem meadBase = new CustomItem(BasePrefabName, baseMeadBase.name, baseConfig);
            if (!IsValidCustomItem(flask) || !IsValidCustomItem(meadBase))
            {
                Logger.LogError("Jotunn could not clone Brennivin flask/base from '" + baseMead.name + "' and '" + baseMeadBase.name + "'.");
                return;
            }

            ConfigureFlask(flask);
            ConfigureMeadBase(meadBase);
            if (!ItemManager.Instance.AddItem(flask) || !ItemManager.Instance.AddItem(meadBase))
            {
                Logger.LogError("Jotunn rejected Brennivin item registration.");
                return;
            }

            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new FermenterConversionConfig
            {
                Station = "Fermenter",
                FromItem = BasePrefabName,
                ToItem = PrefabName,
                ProducedItems = 1
            }));

            _itemRegistered = true;
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterBrennivin;
            Logger.LogInfo("Registered Mead base: Brennivin as a Mead Ketill craft and Brennivin as a one-flask fermenter output.");
        }

        private GameObject ResolveBaseMeadPrefab()
        {
            for (int i = 0; i < MeadBaseCandidates.Length; i++)
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab(MeadBaseCandidates[i]);
                if (IsUsableConsumablePrefab(prefab))
                {
                    return prefab;
                }
            }

            return null;
        }

        private GameObject ResolveBaseMeadBasePrefab()
        {
            for (int i = 0; i < MeadBaseBaseCandidates.Length; i++)
            {
                GameObject prefab = PrefabManager.Instance.GetPrefab(MeadBaseBaseCandidates[i]);
                if (IsUsableItemPrefab(prefab))
                {
                    return prefab;
                }
            }

            return null;
        }

        private static bool IsUsableConsumablePrefab(GameObject prefab)
        {
            ItemDrop itemDrop = GetUsableItemDrop(prefab);
            return itemDrop != null &&
                   itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable;
        }

        private static bool IsUsableItemPrefab(GameObject prefab)
        {
            return GetUsableItemDrop(prefab) != null;
        }

        private static ItemDrop GetUsableItemDrop(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null ||
                itemDrop.m_itemData == null ||
                itemDrop.m_itemData.m_shared == null)
            {
                return null;
            }

            return itemDrop;
        }

        private static bool IsValidCustomItem(CustomItem item)
        {
            return item != null &&
                   item.ItemPrefab != null &&
                   item.ItemDrop != null &&
                   item.ItemDrop.m_itemData != null &&
                   item.ItemDrop.m_itemData.m_shared != null;
        }

        private static void ConfigureFlask(CustomItem item)
        {
            ItemDrop.ItemData data = item.ItemDrop.m_itemData;
            ItemDrop.ItemData.SharedData shared = data.m_shared;

            data.m_dropPrefab = item.ItemPrefab;
            shared.m_name = DisplayName;
            shared.m_description = ItemDescription;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            shared.m_maxStackSize = 10;
            shared.m_weight = 1f;
            shared.m_teleportable = true;
            shared.m_isDrink = true;
            shared.m_food = 0f;
            shared.m_foodStamina = 0f;
            shared.m_foodEitr = 0f;
            shared.m_foodBurnTime = 0f;
            shared.m_foodRegen = 0f;
            shared.m_consumeStatusEffect = null;
            shared.m_equipDuration = 1f;

            TintPrefabCyan(item.ItemPrefab);
        }

        private static void ConfigureMeadBase(CustomItem item)
        {
            ItemDrop.ItemData data = item.ItemDrop.m_itemData;
            ItemDrop.ItemData.SharedData shared = data.m_shared;

            data.m_dropPrefab = item.ItemPrefab;
            shared.m_name = BaseDisplayName;
            shared.m_description = "Ferment this into " + DisplayName + ".";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_maxStackSize = 10;
            shared.m_weight = 1f;
            shared.m_teleportable = true;
            shared.m_isDrink = false;
            shared.m_food = 0f;
            shared.m_foodStamina = 0f;
            shared.m_foodEitr = 0f;
            shared.m_foodBurnTime = 0f;
            shared.m_foodRegen = 0f;
            shared.m_consumeStatusEffect = null;

            TintPrefabCyan(item.ItemPrefab);
        }

        private static void TintPrefabCyan(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }

            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                Material[] materials = renderer.sharedMaterials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material source = materials[m];
                    if (source == null)
                    {
                        continue;
                    }

                    Material copy = new Material(source);
                    copy.name = "Brennivin_Cyan_" + source.name;
                    if (copy.HasProperty("_Color"))
                    {
                        copy.SetColor("_Color", new Color(0.0f, 0.9f, 1f, 1f));
                    }
                    if (copy.HasProperty("_BaseColor"))
                    {
                        copy.SetColor("_BaseColor", new Color(0.0f, 0.9f, 1f, 1f));
                    }
                    if (copy.HasProperty("_EmissionColor"))
                    {
                        copy.SetColor("_EmissionColor", new Color(0.0f, 0.35f, 0.45f, 1f));
                    }
                    materials[m] = copy;
                }
                renderer.sharedMaterials = materials;
            }
        }

        internal static bool IsEnabled()
        {
            return _enabled != null && _enabled.Value;
        }

        internal static bool IsDeathProtectionEnabled()
        {
            return IsEnabled() && _deathProtection != null && _deathProtection.Value;
        }

        internal static bool IsBedTeleportEnabled()
        {
            return IsEnabled() && _bedTeleport != null && _bedTeleport.Value;
        }

        internal static bool IsBrennivin(ItemDrop.ItemData item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.m_dropPrefab != null && string.Equals(GetPrefabName(item.m_dropPrefab.name), PrefabName, StringComparison.Ordinal))
            {
                return true;
            }

            return item.m_shared != null && string.Equals(item.m_shared.m_name, DisplayName, StringComparison.Ordinal);
        }

        private static string GetPrefabName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            int index = name.IndexOfAny(new[] { '(', ' ' });
            return index >= 0 ? name.Substring(0, index) : name;
        }

        internal static bool TryConsumeOne(Inventory inventory)
        {
            if (inventory == null)
            {
                return false;
            }

            var items = inventory.GetAllItems();
            if (items == null)
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (IsBrennivin(item))
                {
                    return inventory.RemoveOneItem(item);
                }
            }

            return false;
        }

        internal static void Message(Player player, string text)
        {
            if (player != null)
            {
                player.Message(MessageHud.MessageType.Center, text);
            }
        }

        internal static void LogInfo(string text)
        {
            if (_logDiagnostics != null && _logDiagnostics.Value && _log != null)
            {
                _log.LogInfo(text);
            }
        }

        internal static void LogWarning(string text)
        {
            if (_log != null)
            {
                _log.LogWarning(text);
            }
        }
    }
}
