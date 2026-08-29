using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// InventoryLink Auto Sort
// Adds a one-click "Sort" button next to the player inventory panel (cloned from the vanilla
// "Stack All" button, so it matches the existing look for free) and another next to the container
// panel (cloned from "Take All"), each wired to re-pack that inventory's grid by category, then
// name, then quality. Equippable items (weapons, tools, shields, armor - anything ItemData.IsEquipable()
// covers) are left untouched in their current slot; only the remaining stackable items get reflowed
// into whatever empty cells are left. A keybind fallback re-sorts whichever panels are currently
// open, in case a cloned button ends up misplaced at your UI scale.

namespace InventoryLink
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class InventorySortPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.inventorylink.autosort";
        public const string PluginName = "InventoryLink Auto Sort";
        public const string PluginVersion = "0.1.0";

        private static readonly Dictionary<ItemDrop.ItemData.ItemType, int> CategoryOrder = new Dictionary<ItemDrop.ItemData.ItemType, int>
        {
            { ItemDrop.ItemData.ItemType.OneHandedWeapon, 0 },
            { ItemDrop.ItemData.ItemType.TwoHandedWeapon, 0 },
            { ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, 0 },
            { ItemDrop.ItemData.ItemType.Bow, 0 },
            { ItemDrop.ItemData.ItemType.Attach_Atgeir, 0 },
            { ItemDrop.ItemData.ItemType.Shield, 1 },
            { ItemDrop.ItemData.ItemType.Helmet, 2 },
            { ItemDrop.ItemData.ItemType.Chest, 2 },
            { ItemDrop.ItemData.ItemType.Legs, 2 },
            { ItemDrop.ItemData.ItemType.Shoulder, 2 },
            { ItemDrop.ItemData.ItemType.Hands, 2 },
            { ItemDrop.ItemData.ItemType.Tool, 3 },
            { ItemDrop.ItemData.ItemType.Utility, 3 },
            { ItemDrop.ItemData.ItemType.Torch, 3 },
            { ItemDrop.ItemData.ItemType.Ammo, 4 },
            { ItemDrop.ItemData.ItemType.AmmoNonEquipable, 4 },
            { ItemDrop.ItemData.ItemType.Consumable, 5 },
            { ItemDrop.ItemData.ItemType.Fish, 5 },
            { ItemDrop.ItemData.ItemType.Trophy, 6 },
            { ItemDrop.ItemData.ItemType.Customization, 7 },
            { ItemDrop.ItemData.ItemType.Trinket, 7 },
            { ItemDrop.ItemData.ItemType.Material, 8 },
            { ItemDrop.ItemData.ItemType.Misc, 9 },
            { ItemDrop.ItemData.ItemType.None, 9 },
        };

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _addSortButtons;
        private static ConfigEntry<KeyboardShortcut> _sortKey;
        private static ConfigEntry<float> _playerButtonOffsetY;
        private static ConfigEntry<float> _containerButtonOffsetY;

        private static ManualLogSource _log;
        private static Button _playerSortButton;
        private static Button _containerSortButton;
        private static FieldInfo _currentContainerField;
        private static MethodInfo _inventoryChangedMethod;

        private Harmony _harmony;

        private void Awake()
        {
            _log = Logger;

            _currentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
            _inventoryChangedMethod = AccessTools.Method(typeof(Inventory), "Changed");

            _enabled = Config.Bind("General", "Enabled", true,
                "Enable auto-sort for the player inventory and open containers.");
            _addSortButtons = Config.Bind("General", "AddSortButtons", true,
                "Clone vanilla buttons into 'Sort' buttons on the inventory/container panels.");
            _sortKey = Config.Bind("General", "SortKey", new KeyboardShortcut(KeyCode.X, KeyCode.LeftAlt),
                "Key combo that sorts the player inventory, and the open container if any.");
            _playerButtonOffsetY = Config.Bind("General", "PlayerButtonOffsetY", 35f,
                "Vertical offset (UI units) of the cloned player-inventory sort button from the Stack All button it's cloned from.");
            _containerButtonOffsetY = Config.Bind("General", "ContainerButtonOffsetY", 35f,
                "Vertical offset (UI units) of the cloned container sort button from the Take All button it's cloned from.");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
        }

        private void Update()
        {
            if (_enabled == null || !_enabled.Value || _sortKey == null || !_sortKey.Value.IsDown())
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                SortInventory(player.GetInventory());
            }

            InventoryGui gui = InventoryGui.instance;
            Container openContainer = gui != null && gui.IsContainerOpen() ? GetCurrentContainer(gui) : null;
            if (openContainer != null)
            {
                SortInventory(openContainer.GetInventory());
            }
        }

        internal static void SetupButtons(InventoryGui gui)
        {
            if (_addSortButtons == null || !_addSortButtons.Value || gui == null)
            {
                return;
            }

            if (_playerSortButton == null)
            {
                if (gui.m_stackAllButton == null)
                {
                    if (_log != null)
                    {
                        _log.LogWarning("Auto Sort: InventoryGui.m_stackAllButton was null; player sort button was not created.");
                    }
                }
                else
                {
                    float offset = _playerButtonOffsetY == null ? 35f : _playerButtonOffsetY.Value;
                    _playerSortButton = CloneButton(gui.m_stackAllButton, offset, "Sort Inv.", "PlayerSortButton", delegate
                    {
                        Player player = Player.m_localPlayer;
                        if (player != null)
                        {
                            SortInventory(player.GetInventory());
                        }
                    });
                }
            }

            if (_containerSortButton == null)
            {
                if (gui.m_takeAllButton == null)
                {
                    if (_log != null)
                    {
                        _log.LogWarning("Auto Sort: InventoryGui.m_takeAllButton was null; container sort button was not created.");
                    }
                }
                else
                {
                    float offset = _containerButtonOffsetY == null ? 35f : _containerButtonOffsetY.Value;
                    _containerSortButton = CloneButton(gui.m_takeAllButton, offset, "Sort", "ContainerSortButton", delegate
                    {
                        InventoryGui current = InventoryGui.instance;
                        Container container = current != null ? GetCurrentContainer(current) : null;
                        if (container != null)
                        {
                            SortInventory(container.GetInventory());
                        }
                    });
                    _containerSortButton.gameObject.SetActive(false);
                }
            }
        }

        private static Container GetCurrentContainer(InventoryGui gui)
        {
            if (_currentContainerField == null || gui == null)
            {
                return null;
            }

            return _currentContainerField.GetValue(gui) as Container;
        }

        internal static void SetContainerButtonVisible(bool visible)
        {
            if (_containerSortButton != null)
            {
                _containerSortButton.gameObject.SetActive(visible);
            }
        }

        private static Button CloneButton(Button template, float offsetY, string label, string cloneName, UnityEngine.Events.UnityAction onClick)
        {
            GameObject clone = Object.Instantiate(template.gameObject, template.transform.parent);
            clone.name = cloneName;
            clone.SetActive(true);

            // A HorizontalLayoutGroup/VerticalLayoutGroup on the parent would otherwise reposition
            // the clone on top of the button it was cloned from, silently ignoring anchoredPosition.
            LayoutElement layoutElement = clone.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = clone.AddComponent<LayoutElement>();
            }
            layoutElement.ignoreLayout = true;

            RectTransform rect = clone.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchoredPosition += new Vector2(0f, offsetY);
            }

            TMP_Text text = clone.GetComponentInChildren<TMP_Text>();
            if (text != null)
            {
                text.text = label;
            }

            Button button = clone.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);

            if (_log != null)
            {
                Vector2 pos = rect != null ? rect.anchoredPosition : Vector2.zero;
                _log.LogInfo("Auto Sort: created '" + cloneName + "' at " + pos + " (parent active=" + clone.transform.parent.gameObject.activeInHierarchy + ").");
            }

            return button;
        }

        internal static void SortInventory(Inventory inventory)
        {
            if (inventory == null)
            {
                return;
            }

            int width = inventory.GetWidth();
            int height = inventory.GetHeight();
            if (width <= 0 || height <= 0)
            {
                return;
            }

            List<ItemDrop.ItemData> allItems = inventory.GetAllItems();
            List<ItemDrop.ItemData> toSort = new List<ItemDrop.ItemData>();
            HashSet<int> keptCells = new HashSet<int>();

            for (int i = 0; i < allItems.Count; i++)
            {
                ItemDrop.ItemData item = allItems[i];
                if (item != null && item.IsEquipable())
                {
                    keptCells.Add(item.m_gridPos.y * width + item.m_gridPos.x);
                }
                else
                {
                    toSort.Add(item);
                }
            }

            toSort.Sort(CompareItems);

            int placed = 0;
            for (int y = 0; y < height && placed < toSort.Count; y++)
            {
                for (int x = 0; x < width && placed < toSort.Count; x++)
                {
                    if (keptCells.Contains(y * width + x))
                    {
                        continue;
                    }

                    toSort[placed].m_gridPos = new Vector2i(x, y);
                    placed++;
                }
            }

            if (_inventoryChangedMethod != null)
            {
                _inventoryChangedMethod.Invoke(inventory, null);
            }

            if (_log != null)
            {
                _log.LogInfo("Sorted " + placed + " item stack(s), left " + keptCells.Count + " equippable item(s) in place.");
            }
        }

        private static int CompareItems(ItemDrop.ItemData a, ItemDrop.ItemData b)
        {
            int categoryCompare = GetCategoryOrder(a).CompareTo(GetCategoryOrder(b));
            if (categoryCompare != 0)
            {
                return categoryCompare;
            }

            string nameA = a.m_shared != null ? a.m_shared.m_name : string.Empty;
            string nameB = b.m_shared != null ? b.m_shared.m_name : string.Empty;
            int nameCompare = string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            if (nameCompare != 0)
            {
                return nameCompare;
            }

            int qualityCompare = b.m_quality.CompareTo(a.m_quality);
            if (qualityCompare != 0)
            {
                return qualityCompare;
            }

            return b.m_stack.CompareTo(a.m_stack);
        }

        private static int GetCategoryOrder(ItemDrop.ItemData item)
        {
            int order;
            if (item != null && item.m_shared != null && CategoryOrder.TryGetValue(item.m_shared.m_itemType, out order))
            {
                return order;
            }

            return 9;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryGuiAwakeSortPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            InventorySortPlugin.SetupButtons(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Show", new Type[] { typeof(Container), typeof(int) })]
    internal static class InventoryGuiShowSortPatch
    {
        private static void Postfix()
        {
            InventorySortPlugin.SetContainerButtonVisible(true);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
    internal static class InventoryGuiCloseContainerSortPatch
    {
        private static void Postfix()
        {
            InventorySortPlugin.SetContainerButtonVisible(false);
        }
    }
}
