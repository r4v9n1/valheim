using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

// InventoryLink
// Author stamp: Created by R4V9N1

[assembly: AssemblyTitle(InventoryLink.InventoryLinkPlugin.PluginName)]
[assembly: AssemblyVersion(InventoryLink.InventoryLinkPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(InventoryLink.InventoryLinkPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(InventoryLink.InventoryLinkPlugin.CreatorCredit)]
[assembly: AssemblyProduct(InventoryLink.InventoryLinkPlugin.PluginName)]
[assembly: AssemblyCopyright(InventoryLink.InventoryLinkPlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", InventoryLink.InventoryLinkPlugin.CreatorCredit)]

namespace InventoryLink
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class InventoryLinkPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.inventorylink";
        public const string PluginName = "InventoryLink";
        public const string PluginVersion = "0.2.8";
        public const string CreatorCredit = "Created by R4V9N1";

        private const string PreviousDefaultCraftingStationNames = "$piece_workbench,$piece_stonecutter";
        private const string DefaultCraftingStationNames = "$piece_workbench,$piece_stonecutter,$piece_forge,$piece_blackforge,$piece_magetable,$piece_artisanstation";
        private const string LightMyFireCoalBarrelPrefab = "R4V9N1_LightMyFireCoalBarrel";
        private const string LightMyFireResinBarrelPrefab = "R4V9N1_LightMyFireResinBarrel";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _pullRadius;
        private static ConfigEntry<bool> _extendCraftingStationBuildRange;
        private static ConfigEntry<float> _craftingStationBuildRange;
        private static ConfigEntry<string> _craftingStationBuildRangeNames;
        private static ConfigEntry<bool> _respectWards;
        private static ConfigEntry<bool> _respectContainerAccess;
        private static ConfigEntry<bool> _skipInUseContainers;
        private static ConfigEntry<bool> _logPulls;

        private static ManualLogSource _log;
        private static FieldInfo _containerNViewField;
        private static FieldInfo _inventoryGuiCraftRecipeField;
        private static MethodInfo _containerLoadMethod;
        private static float _nextContainerFailureLogTime;
        private static int _suppressedContainerFailures;

        private static RequirementCheckContext _requirementCheck;
        private static RequirementDisplayContext _requirementDisplay;
        private static Player _lastPlacedPlayer;
        private static int _lastPlacedFrame = -1;
        private static Player _craftingPlayer;
        private static int _craftingFrame = -1;

        private static Player _cachedPlayer;
        private static int _cachedFrame = -1;
        private static List<ContainerCandidate> _cachedContainers;

        private Harmony _harmony;

        private void Awake()
        {
            _log = Logger;
            _enabled = Config.Bind("General", "Enabled", true, "Enable InventoryLink.");
            _pullRadius = Config.Bind("General", "PullRadius", 50f, "How far from the player to search for linked containers.");
            _extendCraftingStationBuildRange = Config.Bind("Crafting Stations", "ExtendBuildRange", true, "Extend configured crafting station build radii.");
            _craftingStationBuildRange = Config.Bind("Crafting Stations", "BuildRange", 50f, "Minimum build radius for configured crafting stations.");
            _craftingStationBuildRangeNames = Config.Bind("Crafting Stations", "StationNames", DefaultCraftingStationNames, "Comma-separated CraftingStation m_name values to extend. Defaults to the workbench, stonecutter, forge, black forge, Galdr table, and artisan table.");
            _respectWards = Config.Bind("Access", "RespectWards", true, "Skip containers blocked by ward/private-area access.");
            _respectContainerAccess = Config.Bind("Access", "RespectContainerAccess", true, "Respect personal/private container access rules.");
            _skipInUseContainers = Config.Bind("Access", "SkipInUseContainers", true, "Do not pull from containers that another player has open.");
            _logPulls = Config.Bind("Debug", "LogPulls", false, "Log each successful item pull to the BepInEx log.");

            MigrateOldDefaults();

            _containerNViewField = AccessTools.Field(typeof(Container), "m_nview");
            _inventoryGuiCraftRecipeField = AccessTools.Field(typeof(InventoryGui), "m_craftRecipe");
            _containerLoadMethod = AccessTools.Method(typeof(Container), "Load");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
            Logger.LogInfo(CreatorCredit + ".");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        internal static bool IsEnabledFor(Player player)
        {
            if (_enabled == null || !_enabled.Value || player == null)
            {
                return false;
            }

            return Player.m_localPlayer == null || object.ReferenceEquals(Player.m_localPlayer, player);
        }

        internal static void ExtendCraftingStationBuildRange(CraftingStation station, ref float range)
        {
            if (_enabled == null || !_enabled.Value ||
                _extendCraftingStationBuildRange == null || !_extendCraftingStationBuildRange.Value ||
                station == null || !IsConfiguredCraftingStation(station))
            {
                return;
            }

            float configuredRange = _craftingStationBuildRange == null ? 50f : Math.Max(0f, _craftingStationBuildRange.Value);
            if (range < configuredRange)
            {
                range = configuredRange;
            }
        }

        internal static void BeginRequirementCheck(Player player, Player.RequirementMode mode)
        {
            if (!IsEnabledFor(player))
            {
                return;
            }

            if (mode != Player.RequirementMode.CanBuild && mode != Player.RequirementMode.CanAlmostBuild)
            {
                return;
            }

            _requirementCheck = new RequirementCheckContext(
                player,
                player.GetInventory(),
                mode == Player.RequirementMode.CanBuild,
                mode == Player.RequirementMode.CanAlmostBuild);
        }

        internal static void EndRequirementCheck()
        {
            _requirementCheck = null;
        }

        internal static void BeginRecipeRequirementCheck(Player player, Recipe recipe, bool discover)
        {
            if (!IsEnabledFor(player) || discover || !SupportsRecipeAutoPull(recipe))
            {
                return;
            }

            _requirementCheck = new RequirementCheckContext(player, player.GetInventory(), true, false);
        }

        internal static void BeginRequirementDisplay(Player player)
        {
            if (!IsEnabledFor(player))
            {
                return;
            }

            _requirementDisplay = new RequirementDisplayContext(player, player.GetInventory());
        }

        internal static void EndRequirementDisplay()
        {
            _requirementDisplay = null;
        }

        internal static void BeginCrafting(InventoryGui inventoryGui, Player player)
        {
            if (!IsEnabledFor(player))
            {
                return;
            }

            Recipe recipe = GetCraftRecipe(inventoryGui);
            if (!SupportsRecipeAutoPull(recipe))
            {
                return;
            }

            _craftingPlayer = player;
            _craftingFrame = Time.frameCount;
        }

        internal static void EndCrafting()
        {
            _craftingPlayer = null;
            _craftingFrame = -1;
        }

        internal static void AddNearbyCountForRequirement(Inventory inventory, string itemName, int quality, bool matchWorldLevel, ref int count)
        {
            Player player = null;
            Inventory playerInventory = null;

            if (_requirementCheck != null && _requirementCheck.CountAmounts)
            {
                player = _requirementCheck.Player;
                playerInventory = _requirementCheck.PlayerInventory;
            }
            else if (_requirementDisplay != null)
            {
                player = _requirementDisplay.Player;
                playerInventory = _requirementDisplay.PlayerInventory;
            }
            else
            {
                return;
            }

            if (!object.ReferenceEquals(inventory, playerInventory))
            {
                return;
            }

            count += CountNearbyItem(player, itemName, quality, matchWorldLevel, int.MaxValue);
        }

        internal static void AddNearbyHaveItemForRequirement(Inventory inventory, string itemName, bool matchWorldLevel, ref bool haveItem)
        {
            if (haveItem || _requirementCheck == null || !_requirementCheck.CountAny)
            {
                return;
            }

            if (!object.ReferenceEquals(inventory, _requirementCheck.PlayerInventory))
            {
                return;
            }

            haveItem = CountNearbyItem(_requirementCheck.Player, itemName, -1, matchWorldLevel, 1) > 0;
        }

        internal static void MarkPlaced(Player player, bool placed)
        {
            if (!placed || !IsEnabledFor(player))
            {
                return;
            }

            _lastPlacedPlayer = player;
            _lastPlacedFrame = Time.frameCount;
        }

        internal static PullPlan CreatePullPlan(Player player, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier)
        {
            bool buildPlacement = object.ReferenceEquals(_lastPlacedPlayer, player) && _lastPlacedFrame == Time.frameCount;
            bool workbenchCrafting = object.ReferenceEquals(_craftingPlayer, player) && _craftingFrame == Time.frameCount;

            if (!IsEnabledFor(player) || (!buildPlacement && !workbenchCrafting))
            {
                return null;
            }

            if (requirements == null || requirements.Length == 0)
            {
                return null;
            }

            Inventory playerInventory = player.GetInventory();
            if (playerInventory == null)
            {
                return null;
            }

            PullPlan plan = new PullPlan(player);

            for (int i = 0; i < requirements.Length; i++)
            {
                Piece.Requirement requirement = requirements[i];
                if (requirement == null || requirement.m_resItem == null)
                {
                    continue;
                }

                int requiredAmount = requirement.GetAmount(qualityLevel) * multiplier;
                if (requiredAmount <= 0)
                {
                    continue;
                }

                string itemName = requirement.m_resItem.m_itemData.m_shared.m_name;
                int playerAmount = playerInventory.CountItems(itemName, itemQuality, true);
                int shortfall = requiredAmount - playerAmount;
                if (shortfall > 0)
                {
                    plan.Add(itemName, itemQuality, shortfall);
                }
            }

            return plan.HasItems ? plan : null;
        }

        internal static void PayPullPlan(PullPlan plan)
        {
            if (plan == null || !IsEnabledFor(plan.Player))
            {
                return;
            }

            List<ContainerCandidate> containers = FindContainers(plan.Player);
            for (int i = 0; i < plan.Items.Count; i++)
            {
                PullItem item = plan.Items[i];
                int remaining = item.Amount;

                for (int j = 0; j < containers.Count && remaining > 0; j++)
                {
                    remaining -= PullFromContainer(containers[j].Container, item.Name, item.Quality, remaining);
                }

                if (remaining > 0 && _log != null)
                {
                    _log.LogWarning("InventoryLink could not pull " + remaining + "x " + item.Name + " after placement. The container contents may have changed.");
                }
            }

            ClearContainerCache();
        }

        private static int CountNearbyItem(Player player, string itemName, int quality, bool matchWorldLevel, int stopAt)
        {
            if (string.IsNullOrEmpty(itemName) || !IsEnabledFor(player))
            {
                return 0;
            }

            int total = 0;
            List<ContainerCandidate> containers = FindContainers(player);
            for (int i = 0; i < containers.Count; i++)
            {
                Inventory inventory = GetLoadedInventory(containers[i].Container);
                if (inventory == null)
                {
                    continue;
                }

                total += inventory.CountItems(itemName, quality, matchWorldLevel);
                if (total >= stopAt)
                {
                    return total;
                }
            }

            return total;
        }

        private static int PullFromContainer(Container container, string itemName, int quality, int amount)
        {
            if (container == null || amount <= 0)
            {
                return 0;
            }

            ZNetView view = GetContainerView(container);
            if (view != null && view.IsValid() && !view.IsOwner())
            {
                view.ClaimOwnership();
            }

            Inventory inventory = GetLoadedInventory(container);
            if (inventory == null)
            {
                return 0;
            }

            int before = inventory.CountItems(itemName, quality, true);
            int toRemove = Math.Min(before, amount);
            if (toRemove <= 0)
            {
                return 0;
            }

            inventory.RemoveItem(itemName, toRemove, quality, true);
            int after = inventory.CountItems(itemName, quality, true);
            int removed = before - after;

            if (removed > 0 && _log != null && _logPulls != null && _logPulls.Value)
            {
                _log.LogInfo("Pulled " + removed + "x " + itemName + " from " + container.GetHoverName() + ".");
            }

            return Math.Max(removed, 0);
        }

        private static List<ContainerCandidate> FindContainers(Player player)
        {
            if (_cachedContainers != null && object.ReferenceEquals(_cachedPlayer, player) && _cachedFrame == Time.frameCount)
            {
                return _cachedContainers;
            }

            List<ContainerCandidate> result = new List<ContainerCandidate>();
            if (!IsEnabledFor(player))
            {
                return result;
            }

            float radius = _pullRadius == null ? 50f : Math.Max(0f, _pullRadius.Value);
            float radiusSqr = radius * radius;
            Vector3 playerPosition = player.transform.position;
            long playerId = player.GetPlayerID();
            Container[] containers = Object.FindObjectsByType<Container>(FindObjectsSortMode.None);

            for (int i = 0; i < containers.Length; i++)
            {
                Container container = containers[i];
                if (container == null)
                {
                    continue;
                }

                if (IsReservedAutomationContainer(container))
                {
                    continue;
                }

                float distanceSqr = (container.transform.position - playerPosition).sqrMagnitude;
                if (distanceSqr > radiusSqr)
                {
                    continue;
                }

                if (!CanUseContainer(container, playerId))
                {
                    continue;
                }

                result.Add(new ContainerCandidate(container, distanceSqr));
            }

            result.Sort(delegate(ContainerCandidate left, ContainerCandidate right)
            {
                return left.DistanceSqr.CompareTo(right.DistanceSqr);
            });

            _cachedPlayer = player;
            _cachedFrame = Time.frameCount;
            _cachedContainers = result;
            return result;
        }

        private static bool CanUseContainer(Container container, long playerId)
        {
            try
            {
                if (IsReservedAutomationContainer(container))
                {
                    return false;
                }

                ZNetView view;
                if (!TryGetReadyContainer(container, out view))
                {
                    return false;
                }

                if (_skipInUseContainers != null && _skipInUseContainers.Value && container.IsInUse())
                {
                    return false;
                }

                if (_respectWards != null && _respectWards.Value && ContainerChecksGuardStone(container))
                {
                    if (!PrivateArea.CheckAccess(container.transform.position, 0f, true, false))
                    {
                        return false;
                    }
                }

                if (_respectContainerAccess != null && _respectContainerAccess.Value && !CheckContainerAccess(container, playerId))
                {
                    return false;
                }

                return GetLoadedInventory(container) != null;
            }
            catch (Exception ex)
            {
                LogUnexpectedContainerFailure(container, ex);
                return false;
            }
        }

        private static bool IsReservedAutomationContainer(Container container)
        {
            if (container == null || container.gameObject == null)
            {
                return false;
            }

            string objectName = container.gameObject.name ?? string.Empty;
            return objectName.StartsWith(LightMyFireCoalBarrelPrefab, StringComparison.Ordinal) ||
                objectName.StartsWith(LightMyFireResinBarrelPrefab, StringComparison.Ordinal);
        }

        private static bool ContainerChecksGuardStone(Container container)
        {
            return container != null && container.m_checkGuardStone;
        }

        private static bool CheckContainerAccess(Container container, long playerId)
        {
            if (container == null)
            {
                return false;
            }

            switch (container.m_privacy)
            {
                case Container.PrivacySetting.Public:
                    return true;
                case Container.PrivacySetting.Private:
                    Piece piece = container.GetComponent<Piece>();
                    return piece != null && piece.GetCreator() == playerId;
                case Container.PrivacySetting.Group:
                default:
                    return false;
            }
        }

        private static Inventory GetLoadedInventory(Container container)
        {
            try
            {
                ZNetView view;
                if (!TryGetReadyContainer(container, out view))
                {
                    return null;
                }

                LoadContainer(container);
                if (!view.IsValid())
                {
                    return null;
                }

                return container.GetInventory();
            }
            catch (Exception ex)
            {
                LogUnexpectedContainerFailure(container, ex);
                return null;
            }
        }

        private static void LoadContainer(Container container)
        {
            if (_containerLoadMethod != null)
            {
                _containerLoadMethod.Invoke(container, null);
            }
        }

        private static ZNetView GetContainerView(Container container)
        {
            if (_containerNViewField == null || container == null)
            {
                return null;
            }

            return _containerNViewField.GetValue(container) as ZNetView;
        }

        private static bool TryGetReadyContainer(Container container, out ZNetView view)
        {
            view = null;
            if (container == null || !container.isActiveAndEnabled)
            {
                return false;
            }

            view = GetContainerView(container);
            return view != null && view.IsValid() && container.GetInventory() != null;
        }

        private static void LogUnexpectedContainerFailure(Container container, Exception exception)
        {
            if (_log == null || exception == null)
            {
                return;
            }

            while (exception is TargetInvocationException && exception.InnerException != null)
            {
                exception = exception.InnerException;
            }

            float now = Time.unscaledTime;
            if (now < _nextContainerFailureLogTime)
            {
                _suppressedContainerFailures++;
                return;
            }

            string containerName = container == null ? "<destroyed>" : container.gameObject.name;
            string suppressed = _suppressedContainerFailures > 0
                ? " (" + _suppressedContainerFailures + " similar failures suppressed)"
                : "";
            _suppressedContainerFailures = 0;
            _nextContainerFailureLogTime = now + 5f;
            _log.LogDebug("Skipped unavailable container '" + containerName + "': " +
                exception.GetType().Name + ": " + exception.Message + suppressed);
        }

        private static Recipe GetCraftRecipe(InventoryGui inventoryGui)
        {
            if (_inventoryGuiCraftRecipeField == null || inventoryGui == null)
            {
                return null;
            }

            return _inventoryGuiCraftRecipeField.GetValue(inventoryGui) as Recipe;
        }

        private static bool SupportsRecipeAutoPull(Recipe recipe)
        {
            return recipe != null && !recipe.m_requireOnlyOneIngredient;
        }

        private static bool IsConfiguredCraftingStation(CraftingStation station)
        {
            if (station == null)
            {
                return false;
            }

            string names = _craftingStationBuildRangeNames == null ? DefaultCraftingStationNames : _craftingStationBuildRangeNames.Value;
            if (string.IsNullOrEmpty(names))
            {
                return false;
            }

            string stationName = station.m_name ?? "";
            string[] parts = names.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string configured = parts[i].Trim();
                if (configured.Length == 0)
                {
                    continue;
                }

                if (string.Equals(stationName, configured, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void MigrateOldDefaults()
        {
            bool changed = false;
            if (_pullRadius != null && Math.Abs(_pullRadius.Value - 30f) < 0.001f)
            {
                _pullRadius.Value = 50f;
                changed = true;
            }

            if (_craftingStationBuildRangeNames != null &&
                string.Equals(_craftingStationBuildRangeNames.Value, PreviousDefaultCraftingStationNames, StringComparison.Ordinal))
            {
                _craftingStationBuildRangeNames.Value = DefaultCraftingStationNames;
                changed = true;
            }

            if (changed)
            {
                Config.Save();
            }
        }

        private static void ClearContainerCache()
        {
            _cachedPlayer = null;
            _cachedFrame = -1;
            _cachedContainers = null;
        }

        internal sealed class RequirementCheckContext
        {
            public readonly Player Player;
            public readonly Inventory PlayerInventory;
            public readonly bool CountAmounts;
            public readonly bool CountAny;

            public RequirementCheckContext(Player player, Inventory playerInventory, bool countAmounts, bool countAny)
            {
                Player = player;
                PlayerInventory = playerInventory;
                CountAmounts = countAmounts;
                CountAny = countAny;
            }
        }

        internal sealed class RequirementDisplayContext
        {
            public readonly Player Player;
            public readonly Inventory PlayerInventory;

            public RequirementDisplayContext(Player player, Inventory playerInventory)
            {
                Player = player;
                PlayerInventory = playerInventory;
            }
        }

        internal sealed class PullPlan
        {
            public readonly Player Player;
            public readonly List<PullItem> Items = new List<PullItem>();

            public PullPlan(Player player)
            {
                Player = player;
            }

            public bool HasItems
            {
                get { return Items.Count > 0; }
            }

            public void Add(string name, int quality, int amount)
            {
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Name == name && Items[i].Quality == quality)
                    {
                        Items[i].Amount += amount;
                        return;
                    }
                }

                Items.Add(new PullItem(name, quality, amount));
            }
        }

        internal sealed class PullItem
        {
            public readonly string Name;
            public readonly int Quality;
            public int Amount;

            public PullItem(string name, int quality, int amount)
            {
                Name = name;
                Quality = quality;
                Amount = amount;
            }
        }

        private sealed class ContainerCandidate
        {
            public readonly Container Container;
            public readonly float DistanceSqr;

            public ContainerCandidate(Container container, float distanceSqr)
            {
                Container = container;
                DistanceSqr = distanceSqr;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "HaveRequirements", new Type[] { typeof(Piece), typeof(Player.RequirementMode) })]
    internal static class PlayerHaveRequirementsPatch
    {
        private static void Prefix(Player __instance, Player.RequirementMode mode)
        {
            InventoryLinkPlugin.BeginRequirementCheck(__instance, mode);
        }

        private static void Postfix()
        {
            InventoryLinkPlugin.EndRequirementCheck();
        }
    }

    [HarmonyPatch(typeof(Player), "HaveRequirements", new Type[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
    internal static class PlayerHaveRecipeRequirementsPatch
    {
        private static void Prefix(Player __instance, Recipe recipe, bool discover)
        {
            InventoryLinkPlugin.BeginRecipeRequirementCheck(__instance, recipe, discover);
        }

        private static void Postfix()
        {
            InventoryLinkPlugin.EndRequirementCheck();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "SetupRequirement", new Type[] { typeof(Transform), typeof(Piece.Requirement), typeof(Player), typeof(bool), typeof(int), typeof(int) })]
    internal static class InventoryGuiSetupRequirementPatch
    {
        private static void Prefix(Player player)
        {
            InventoryLinkPlugin.BeginRequirementDisplay(player);
        }

        private static void Postfix()
        {
            InventoryLinkPlugin.EndRequirementDisplay();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting", new Type[] { typeof(Player) })]
    internal static class InventoryGuiDoCraftingPatch
    {
        private static void Prefix(InventoryGui __instance, Player player)
        {
            InventoryLinkPlugin.BeginCrafting(__instance, player);
        }

        private static void Postfix()
        {
            InventoryLinkPlugin.EndCrafting();
        }
    }

    [HarmonyPatch(typeof(Inventory), "CountItems", new Type[] { typeof(string), typeof(int), typeof(bool) })]
    internal static class InventoryCountItemsPatch
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            InventoryLinkPlugin.AddNearbyCountForRequirement(__instance, name, quality, matchWorldLevel, ref __result);
        }
    }

    [HarmonyPatch(typeof(Inventory), "HaveItem", new Type[] { typeof(string), typeof(bool) })]
    internal static class InventoryHaveItemPatch
    {
        private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            InventoryLinkPlugin.AddNearbyHaveItemForRequirement(__instance, name, matchWorldLevel, ref __result);
        }
    }

    [HarmonyPatch(typeof(CraftingStation), "GetStationBuildRange")]
    internal static class CraftingStationGetStationBuildRangePatch
    {
        private static void Postfix(CraftingStation __instance, ref float __result)
        {
            InventoryLinkPlugin.ExtendCraftingStationBuildRange(__instance, ref __result);
        }
    }

    [HarmonyPatch(typeof(Player), "TryPlacePiece", new Type[] { typeof(Piece) })]
    internal static class PlayerTryPlacePiecePatch
    {
        private static void Postfix(Player __instance, bool __result)
        {
            InventoryLinkPlugin.MarkPlaced(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Player), "ConsumeResources", new Type[] { typeof(Piece.Requirement[]), typeof(int), typeof(int), typeof(int) })]
    internal static class PlayerConsumeResourcesPatch
    {
        private static void Prefix(Player __instance, Piece.Requirement[] requirements, int qualityLevel, int itemQuality, int multiplier, ref InventoryLinkPlugin.PullPlan __state)
        {
            __state = InventoryLinkPlugin.CreatePullPlan(__instance, requirements, qualityLevel, itemQuality, multiplier);
        }

        private static void Postfix(InventoryLinkPlugin.PullPlan __state)
        {
            InventoryLinkPlugin.PayPullPlan(__state);
        }
    }
}
