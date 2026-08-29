using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace LightMyFire
{
    internal static class LightMyFireItemFilter
    {
        private static readonly ConditionalWeakTable<Inventory, StrongBox<string>> FilteredInventories =
            new ConditionalWeakTable<Inventory, StrongBox<string>>();

        internal static void RegisterFilteredInventory(Inventory inventory, string fuelItemName)
        {
            if (inventory == null || string.IsNullOrEmpty(fuelItemName))
            {
                return;
            }

            StrongBox<string> box;
            if (FilteredInventories.TryGetValue(inventory, out box))
            {
                box.Value = fuelItemName;
            }
            else
            {
                FilteredInventories.Add(inventory, new StrongBox<string>(fuelItemName));
            }
        }

        internal static void UnregisterFilteredInventory(Inventory inventory)
        {
            if (inventory != null)
            {
                FilteredInventories.Remove(inventory);
            }
        }

        internal static bool TryGetExpectedFuel(Inventory inventory, out string fuelItemName)
        {
            fuelItemName = null;
            if (inventory == null)
            {
                return false;
            }

            StrongBox<string> box;
            if (!FilteredInventories.TryGetValue(inventory, out box) || box == null || string.IsNullOrEmpty(box.Value))
            {
                return false;
            }

            fuelItemName = box.Value;
            return true;
        }

        internal static bool IsRejected(Inventory inventory, ItemDrop.ItemData item)
        {
            string expected;
            if (!TryGetExpectedFuel(inventory, out expected) || item == null || item.m_shared == null)
            {
                return false;
            }

            return !IsAcceptedFuelName(expected, item.m_shared.m_name);
        }

        internal static bool IsRejectedInvocation(Inventory inventory, MethodBase method, object[] args, out string rejectedItemName, out string expectedFuelName)
        {
            rejectedItemName = null;
            expectedFuelName = null;

            if (!TryGetExpectedFuel(inventory, out expectedFuelName) || method == null || args == null)
            {
                return false;
            }

            ParameterInfo[] parameters = method.GetParameters();

            // First handle concrete item carriers. These are the normal drag/drop, shift-click,
            // stack-transfer and CanAddItem paths used by Valheim's inventory UI.
            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                if (arg == null)
                {
                    continue;
                }

                ItemDrop.ItemData itemData = arg as ItemDrop.ItemData;
                if (itemData != null && itemData.m_shared != null)
                {
                    string actual = itemData.m_shared.m_name;
                    if (!IsAcceptedFuelName(expectedFuelName, actual))
                    {
                        rejectedItemName = actual;
                        return true;
                    }
                    continue;
                }

                ItemDrop itemDrop = arg as ItemDrop;
                if (itemDrop != null && itemDrop.m_itemData != null && itemDrop.m_itemData.m_shared != null)
                {
                    string actual = itemDrop.m_itemData.m_shared.m_name;
                    if (!IsAcceptedFuelName(expectedFuelName, actual))
                    {
                        rejectedItemName = actual;
                        return true;
                    }
                    continue;
                }

                GameObject gameObject = arg as GameObject;
                if (gameObject != null)
                {
                    ItemDrop drop = gameObject.GetComponent<ItemDrop>();
                    if (drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null)
                    {
                        string actual = drop.m_itemData.m_shared.m_name;
                        if (!IsAcceptedFuelName(expectedFuelName, actual))
                        {
                            rejectedItemName = actual;
                            return true;
                        }
                    }
                }
            }

            // Some Valheim versions expose AddItem(string name, ...). Patch that path too. We only
            // consider parameters that are actually item-name-like, never crafter/player names.
            if (string.Equals(method.Name, "AddItem", StringComparison.Ordinal) ||
                string.Equals(method.Name, "CanAddItem", StringComparison.Ordinal))
            {
                int count = Math.Min(parameters.Length, args.Length);
                for (int i = 0; i < count; i++)
                {
                    if (parameters[i].ParameterType != typeof(string) || !(args[i] is string))
                    {
                        continue;
                    }

                    string parameterName = parameters[i].Name ?? string.Empty;
                    string lower = parameterName.ToLowerInvariant();
                    if (lower.Contains("crafter") || lower.Contains("player") || lower.Contains("creator"))
                    {
                        continue;
                    }

                    bool itemNameParameter = lower == "name" || lower.Contains("item") || lower.Contains("prefab") || lower.Contains("shared");
                    if (!itemNameParameter)
                    {
                        continue;
                    }

                    string actual = (string)args[i];
                    if (!IsAcceptedFuelName(expectedFuelName, actual))
                    {
                        rejectedItemName = actual;
                        return true;
                    }

                    // The first item-name parameter determines the item being inserted.
                    break;
                }
            }

            // Finally protect whole-inventory transfer overloads. If a caller tries to dump a mixed
            // inventory into a fuel barrel, reject the operation as a whole instead of letting the
            // wrong stacks slip through a bulk path that never exposes an ItemData argument.
            if (IsBulkMoveMethod(method.Name))
            {
                for (int i = 0; i < args.Length; i++)
                {
                    Inventory source = args[i] as Inventory;
                    if (source == null || object.ReferenceEquals(source, inventory))
                    {
                        continue;
                    }

                    List<ItemDrop.ItemData> sourceItems = source.GetAllItems();
                    if (sourceItems == null)
                    {
                        continue;
                    }

                    for (int itemIndex = 0; itemIndex < sourceItems.Count; itemIndex++)
                    {
                        ItemDrop.ItemData sourceItem = sourceItems[itemIndex];
                        if (sourceItem == null || sourceItem.m_shared == null)
                        {
                            continue;
                        }

                        string actual = sourceItem.m_shared.m_name;
                        if (!IsAcceptedFuelName(expectedFuelName, actual))
                        {
                            rejectedItemName = actual;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool IsBulkMoveMethod(string methodName)
        {
            return string.Equals(methodName, "MoveAll", StringComparison.Ordinal) ||
                   string.Equals(methodName, "MoveInventoryToThis", StringComparison.Ordinal) ||
                   string.Equals(methodName, "MoveAllToThis", StringComparison.Ordinal);
        }

        internal static bool IsAcceptedFuelName(string expectedFuelName, string actualItemName)
        {
            if (string.IsNullOrEmpty(expectedFuelName) || string.IsNullOrEmpty(actualItemName))
            {
                return false;
            }

            if (string.Equals(expectedFuelName, actualItemName, StringComparison.Ordinal))
            {
                return true;
            }

            // Accept common prefab-name aliases for compatibility with mods that call AddItem by
            // prefab name instead of the localized shared-name token. The barrel still has exactly
            // one logical allowed fuel type.
            if (string.Equals(expectedFuelName, LightMyFirePlugin.CoalItemName, StringComparison.Ordinal))
            {
                return string.Equals(actualItemName, "Coal", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(expectedFuelName, LightMyFirePlugin.ResinItemName, StringComparison.Ordinal))
            {
                return string.Equals(actualItemName, "Resin", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }

    /// <summary>
    /// Version-resilient inventory gate. Rather than hard-coding one Valheim Inventory signature,
    /// this patches every current insertion/check overload capable of carrying an item, item name,
    /// prefab or source inventory. The target inventory itself decides which single fuel is allowed.
    /// </summary>
    internal static class LightMyFireHarmonyPatches
    {
        private static Harmony _harmony;

        internal static void Apply(BepInEx.Logging.ManualLogSource log)
        {
            _harmony = new Harmony(LightMyFirePlugin.PluginGuid);
            PatchCurrentInventoryEntryPoints(log);
        }

        internal static void Unpatch()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private static void PatchCurrentInventoryEntryPoints(BepInEx.Logging.ManualLogSource log)
        {
            int patched = 0;
            var seen = new HashSet<MethodBase>();
            List<MethodInfo> methods = AccessTools.GetDeclaredMethods(typeof(Inventory));

            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || seen.Contains(method) || !IsInsertionOrCheckMethod(method.Name))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                bool carriesFilterableData = false;
                for (int p = 0; p < parameters.Length; p++)
                {
                    Type parameterType = parameters[p].ParameterType;
                    if (typeof(ItemDrop.ItemData).IsAssignableFrom(parameterType) ||
                        typeof(ItemDrop).IsAssignableFrom(parameterType) ||
                        typeof(GameObject).IsAssignableFrom(parameterType) ||
                        parameterType == typeof(string) ||
                        (IsBulkMoveMethod(method.Name) && typeof(Inventory).IsAssignableFrom(parameterType)))
                    {
                        carriesFilterableData = true;
                        break;
                    }
                }

                if (!carriesFilterableData)
                {
                    continue;
                }

                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(InventoryItemGatePatch), nameof(InventoryItemGatePatch.Prefix)));
                    seen.Add(method);
                    patched++;
                }
                catch (Exception ex)
                {
                    log.LogWarning("[LightMyFire] Could not patch current Inventory method " + Describe(method) + ": " + ex.Message);
                }
            }

            if (patched > 0)
            {
                log.LogInfo("[LightMyFire] Strict fuel-only item filter attached to " + patched + " current Inventory insertion/check method(s).");
            }
            else
            {
                log.LogWarning("[LightMyFire] No compatible Inventory insertion methods were found to patch. Owner-side barrel sanitation remains active as a safety net.");
            }
        }

        private static bool IsInsertionOrCheckMethod(string methodName)
        {
            return string.Equals(methodName, "AddItem", StringComparison.Ordinal) ||
                   string.Equals(methodName, "CanAddItem", StringComparison.Ordinal) ||
                   string.Equals(methodName, "MoveItemToThis", StringComparison.Ordinal) ||
                   IsBulkMoveMethod(methodName);
        }

        private static bool IsBulkMoveMethod(string methodName)
        {
            return string.Equals(methodName, "MoveAll", StringComparison.Ordinal) ||
                   string.Equals(methodName, "MoveInventoryToThis", StringComparison.Ordinal) ||
                   string.Equals(methodName, "MoveAllToThis", StringComparison.Ordinal);
        }

        private static string Describe(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            string[] names = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                names[i] = parameters[i].ParameterType.Name;
            }
            return method.Name + "(" + string.Join(", ", names) + ")";
        }

        private static class InventoryItemGatePatch
        {
            public static bool Prefix(Inventory __instance, MethodBase __originalMethod, object[] __args)
            {
                string rejected;
                string expected;
                if (!LightMyFireItemFilter.IsRejectedInvocation(__instance, __originalMethod, __args, out rejected, out expected))
                {
                    return true;
                }

                LightMyFirePlugin.LogRejectedInsertion(expected, rejected, __originalMethod == null ? "Inventory" : __originalMethod.Name);
                return false;
            }
        }
    }
}
