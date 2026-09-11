using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

// EquipmentSheet
// Author stamp: Created by R4V9N1

[assembly: AssemblyTitle(EquipmentSheet.EquipmentSheetPlugin.PluginName)]
[assembly: AssemblyVersion(EquipmentSheet.EquipmentSheetPlugin.PluginVersion)]
[assembly: AssemblyFileVersion(EquipmentSheet.EquipmentSheetPlugin.PluginVersion)]
[assembly: AssemblyCompany("R4V9N1")]
[assembly: AssemblyDescription(EquipmentSheet.EquipmentSheetPlugin.CreatorCredit)]
[assembly: AssemblyProduct(EquipmentSheet.EquipmentSheetPlugin.PluginName)]
[assembly: AssemblyCopyright(EquipmentSheet.EquipmentSheetPlugin.CreatorCredit)]
[assembly: AssemblyMetadata("Creator", EquipmentSheet.EquipmentSheetPlugin.CreatorCredit)]

namespace EquipmentSheet
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class EquipmentSheetPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "r4v9n1.equipmentsheet";
        public const string PluginName = "Equipment Sheet";
        public const string PluginVersion = "1.0.2";
        public const string CreatorCredit = "Created by R4V9N1";

        private const string CustomDataKey = "is.codex.valheim.equipmentsheet.inventory.v1";
        private const string PendingTransferKey = "is.codex.valheim.equipmentsheet.pending-transfer.v1";
        private const int SheetColumns = 3;
        private const int SheetRows = 3;
        private const int EquipmentSlotCount = 6;

        private enum SlotKind
        {
            Equipment,
            Food
        }

        private sealed class SlotDef
        {
            public readonly string Label;
            public readonly ItemDrop.ItemData.ItemType Type;
            public readonly SlotKind Kind;

            public SlotDef(string label, ItemDrop.ItemData.ItemType type)
            {
                Label = label;
                Type = type;
                Kind = SlotKind.Equipment;
            }

            public SlotDef(string label)
            {
                Label = label;
                Type = ItemDrop.ItemData.ItemType.None;
                Kind = SlotKind.Food;
            }
        }

        private sealed class SheetUpgradeTransaction
        {
            public Player Player;
            public Inventory PlayerInventory;
            public ItemDrop.ItemData OriginalItem;
            public Vector2i SheetPosition;
            public bool OriginalRemoved;
            public ItemDrop.ItemData ReplacementItem;
        }

        private static readonly SlotDef[] Slots =
        {
            new SlotDef("Helm", ItemDrop.ItemData.ItemType.Helmet),
            new SlotDef("Chest", ItemDrop.ItemData.ItemType.Chest),
            new SlotDef("Legs", ItemDrop.ItemData.ItemType.Legs),
            new SlotDef("Trinket", ItemDrop.ItemData.ItemType.Trinket),
            new SlotDef("Back", ItemDrop.ItemData.ItemType.Shoulder),
            new SlotDef("Belt", ItemDrop.ItemData.ItemType.Utility),
            new SlotDef("Food 1"),
            new SlotDef("Food 2"),
            new SlotDef("Food 3"),
        };

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _panelGap;
        private static ConfigEntry<float> _panelExtraRightOffset;

        private static ManualLogSource _log;
        private static RectTransform _panel;
        private static InventoryGrid _grid;
        private static Inventory _equipmentInventory;
        private static Image[] _slotFrames;
        private static Image[] _slotIcons;
        private static Text[] _slotLabels;
        private static Text[] _slotAmounts;
        private static Text[] _slotQualities;
        private static Image[] _slotDurabilityBacks;
        private static Image[] _slotDurabilityBars;
        private static UIInputHandler[] _slotHitboxes;
        private static UITooltip[] _slotTooltips;
        private static Vector2 _cellSize = new Vector2(50f, 50f);
        private static Vector2[] _slotPositions;

        private static FieldInfo _customDataField;
        private static FieldInfo _onChangedField;
        private static FieldInfo _inventoryItemsField;
        private static FieldInfo _dragGoField;
        private static FieldInfo _dragItemField;
        private static FieldInfo _dragInventoryField;
        private static FieldInfo _dragAmountField;
        private static FieldInfo _itemEquippedField;
        private static FieldInfo _gridWidthField;
        private static FieldInfo _gridHeightField;
        private static FieldInfo _humanoidChestField;
        private static FieldInfo _humanoidLegField;
        private static FieldInfo _humanoidHelmetField;
        private static FieldInfo _humanoidShoulderField;
        private static FieldInfo _humanoidUtilityField;
        private static FieldInfo _humanoidTrinketField;
        private static FieldInfo _currentContainerField;
        private static FieldInfo _craftUpgradeItemField;
        private static MethodInfo _inventoryChangedMethod;
        private static MethodInfo _setupDragItemMethod;
        private static MethodInfo _setupEquipmentMethod;
        private static MethodInfo _createItemTooltipMethod;

        private static Player _loadedPlayer;
        private static bool _loadingInventory;
        private static int _upgradeRecipeScanDepth;
        private static SheetUpgradeTransaction _sheetUpgradeTransaction;
        private static readonly List<ItemDrop.ItemData> PendingAutoRouteItems = new List<ItemDrop.ItemData>();
        private static ItemDrop.ItemData _manualSheetDragItem;
        private static float _lastWrongTypeMessageTime = float.NegativeInfinity;

        private Harmony _harmony;

        private void Awake()
        {
            _log = Logger;

            _enabled = Config.Bind("General", "Enabled", true,
                "Show the equipment sheet panel next to the player inventory.");
            _panelGap = Config.Bind("General", "PanelGap", 20f,
                "Horizontal gap (UI units) between the vanilla inventory grid and the equipment panel.");
            _panelExtraRightOffset = Config.Bind("General", "PanelExtraRightOffset", 60f,
                "Extra horizontal offset added after PanelGap. Useful when another UI overlaps the equipment panel.");

            _customDataField = AccessTools.Field(typeof(Player), "m_customData");
            _onChangedField = AccessTools.Field(typeof(Inventory), "m_onChanged");
            _inventoryItemsField = AccessTools.Field(typeof(Inventory), "m_inventory");
            _dragGoField = AccessTools.Field(typeof(InventoryGui), "m_dragGo");
            _dragItemField = AccessTools.Field(typeof(InventoryGui), "m_dragItem");
            _dragInventoryField = AccessTools.Field(typeof(InventoryGui), "m_dragInventory");
            _dragAmountField = AccessTools.Field(typeof(InventoryGui), "m_dragAmount");
            _itemEquippedField = AccessTools.Field(typeof(ItemDrop.ItemData), "m_equipped");
            _gridWidthField = AccessTools.Field(typeof(InventoryGrid), "m_width");
            _gridHeightField = AccessTools.Field(typeof(InventoryGrid), "m_height");
            _humanoidChestField = AccessTools.Field(typeof(Humanoid), "m_chestItem");
            _humanoidLegField = AccessTools.Field(typeof(Humanoid), "m_legItem");
            _humanoidHelmetField = AccessTools.Field(typeof(Humanoid), "m_helmetItem");
            _humanoidShoulderField = AccessTools.Field(typeof(Humanoid), "m_shoulderItem");
            _humanoidUtilityField = AccessTools.Field(typeof(Humanoid), "m_utilityItem");
            _humanoidTrinketField = AccessTools.Field(typeof(Humanoid), "m_trinketItem");
            _currentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
            _craftUpgradeItemField = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
            _inventoryChangedMethod = AccessTools.Method(typeof(Inventory), "Changed",
                new Type[] { typeof(bool), typeof(bool) });
            _setupDragItemMethod = AccessTools.Method(typeof(InventoryGui), "SetupDragItem",
                new Type[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) });
            _setupEquipmentMethod = AccessTools.Method(typeof(Humanoid), "SetupEquipment");
            _createItemTooltipMethod = AccessTools.Method(typeof(InventoryGrid), "CreateItemTooltip",
                new Type[] { typeof(ItemDrop.ItemData), typeof(UITooltip) });

            EnsureEquipmentInventory();

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

        private void Update()
        {
            InventoryGui gui = InventoryGui.instance;
            Player player = Player.m_localPlayer;

            if (player != null && player != _loadedPlayer)
            {
                LoadEquipmentInventory(player);
                RestorePendingTransfer(player);
                _loadedPlayer = player;
            }

            if (_manualSheetDragItem != null && (gui == null || _dragItemField == null
                || _dragItemField.GetValue(gui) != _manualSheetDragItem))
            {
                _manualSheetDragItem = null;
            }

            ProcessPendingAutoRouteItems(player);

            if (_panel == null || gui == null || player == null)
            {
                return;
            }

            bool shouldShow = _enabled != null && _enabled.Value
                && gui.m_player != null && gui.m_player.gameObject.activeInHierarchy;

            if (_panel.gameObject.activeSelf != shouldShow)
            {
                _panel.gameObject.SetActive(shouldShow);
            }

            if (!shouldShow)
            {
                return;
            }

            UpdateEquipmentGrid(gui, player);
        }

        internal static void SetupPanel(InventoryGui gui)
        {
            if (_panel != null || gui == null || gui.m_player == null
                || gui.m_playerGrid == null || gui.m_playerGrid.m_elementPrefab == null)
            {
                return;
            }

            EnsureEquipmentInventory();

            RectTransform playerRect = gui.m_player;
            GameObject panelGO = new GameObject("EquipmentSheetPanel", typeof(RectTransform));
            panelGO.transform.SetParent(playerRect.parent, false);

            RectTransform panelRect = panelGO.GetComponent<RectTransform>();
            panelRect.anchorMin = playerRect.anchorMin;
            panelRect.anchorMax = playerRect.anchorMax;
            panelRect.pivot = playerRect.pivot;

            RectTransform prefabRect = gui.m_playerGrid.m_elementPrefab.GetComponent<RectTransform>();
            if (prefabRect != null && prefabRect.sizeDelta.x > 0f && prefabRect.sizeDelta.y > 0f)
            {
                _cellSize = prefabRect.sizeDelta;
            }

            const float spacing = 4f;
            const float padding = 12f;
            const int columns = SheetColumns;
            const int rows = SheetRows;

            float panelWidth = columns * _cellSize.x + (columns - 1) * spacing + padding * 2f;
            float panelHeight = rows * _cellSize.y + (rows - 1) * spacing + padding * 2f;
            float gap = (_panelGap == null ? 20f : _panelGap.Value)
                + (_panelExtraRightOffset == null ? 60f : _panelExtraRightOffset.Value);

            panelRect.sizeDelta = new Vector2(panelWidth, panelHeight);
            panelRect.anchoredPosition = playerRect.anchoredPosition + new Vector2(playerRect.rect.width + gap, 0f);

            Image panelImage = panelGO.AddComponent<Image>();
            Image sourcePanelImage = playerRect.GetComponent<Image>();
            if (sourcePanelImage != null)
            {
                panelImage.sprite = sourcePanelImage.sprite;
                panelImage.type = sourcePanelImage.type;
                panelImage.color = sourcePanelImage.color;
                panelImage.material = sourcePanelImage.material;
            }
            else
            {
                panelImage.color = new Color(0f, 0f, 0f, 0.6f);
            }

            GameObject gridRootGO = new GameObject("EquipmentSheetGridRoot", typeof(RectTransform));
            gridRootGO.transform.SetParent(panelRect, false);
            RectTransform gridRoot = gridRootGO.GetComponent<RectTransform>();
            gridRoot.anchorMin = new Vector2(0f, 1f);
            gridRoot.anchorMax = new Vector2(0f, 1f);
            gridRoot.pivot = new Vector2(0f, 1f);
            gridRoot.anchoredPosition = new Vector2(padding, -padding);
            gridRoot.sizeDelta = new Vector2(columns * _cellSize.x + (columns - 1) * spacing,
                rows * _cellSize.y + (rows - 1) * spacing);

            _grid = gridRootGO.AddComponent<InventoryGrid>();
            _grid.m_elementPrefab = gui.m_playerGrid.m_elementPrefab;
            _grid.m_gridRoot = gridRoot;
            if (_gridWidthField != null)
            {
                _gridWidthField.SetValue(_grid, columns);
            }

            if (_gridHeightField != null)
            {
                _gridHeightField.SetValue(_grid, rows);
            }
            _grid.m_elementSpace = spacing;
            _grid.m_tooltipAnchor = gui.m_playerGrid.m_tooltipAnchor;
            _grid.m_onSelected = delegate(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
            {
                HandleEquipmentGridSelected(gui, grid, item, pos, mod);
            };
            _grid.m_onRightClick = delegate(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
            {
                HandleEquipmentGridSelected(gui, grid, item, pos, InventoryGrid.Modifier.Select);
            };
            _grid.OnMoveToLowerInventoryGrid = gui.m_playerGrid.OnMoveToLowerInventoryGrid;
            _grid.OnMoveToUpperInventoryGrid = gui.m_playerGrid.OnMoveToUpperInventoryGrid;
            _grid.m_uiGroup = gui.m_playerGrid.m_uiGroup;

            _slotPositions = new Vector2[Slots.Length];
            _slotFrames = new Image[Slots.Length];
            _slotIcons = new Image[Slots.Length];
            _slotLabels = new Text[Slots.Length];
            _slotAmounts = new Text[Slots.Length];
            _slotQualities = new Text[Slots.Length];
            _slotDurabilityBacks = new Image[Slots.Length];
            _slotDurabilityBars = new Image[Slots.Length];
            _slotHitboxes = new UIInputHandler[Slots.Length];
            _slotTooltips = new UITooltip[Slots.Length];
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            for (int i = 0; i < Slots.Length; i++)
            {
                int x = i % columns;
                int y = i / columns;
                _slotPositions[i] = new Vector2(x * (_cellSize.x + spacing), -y * (_cellSize.y + spacing));

                GameObject frameGO = new GameObject("EquipmentSheet_" + Slots[i].Label + "_Frame", typeof(RectTransform));
                frameGO.transform.SetParent(gridRoot, false);
                RectTransform frameRect = frameGO.GetComponent<RectTransform>();
                frameRect.anchorMin = new Vector2(0f, 1f);
                frameRect.anchorMax = new Vector2(0f, 1f);
                frameRect.pivot = new Vector2(0f, 1f);
                frameRect.anchoredPosition = _slotPositions[i];
                frameRect.sizeDelta = _cellSize;

                Image frameImage = frameGO.AddComponent<Image>();
                frameImage.color = new Color(0f, 0f, 0f, 0.45f);
                frameImage.raycastTarget = false;
                _slotFrames[i] = frameImage;

                GameObject iconGO = new GameObject("EquipmentSheet_" + Slots[i].Label + "_Icon", typeof(RectTransform));
                iconGO.transform.SetParent(gridRoot, false);
                RectTransform iconRect = iconGO.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 1f);
                iconRect.anchorMax = new Vector2(0f, 1f);
                iconRect.pivot = new Vector2(0f, 1f);
                iconRect.anchoredPosition = _slotPositions[i] + new Vector2(4f, -4f);
                iconRect.sizeDelta = _cellSize - new Vector2(8f, 8f);

                Image iconImage = iconGO.AddComponent<Image>();
                iconImage.color = Color.white;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                _slotIcons[i] = iconImage;

                GameObject labelGO = new GameObject("EquipmentSheet_" + Slots[i].Label + "_Label", typeof(RectTransform));
                labelGO.transform.SetParent(gridRoot, false);
                labelGO.transform.SetAsLastSibling();

                RectTransform labelRect = labelGO.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 1f);
                labelRect.anchorMax = new Vector2(0f, 1f);
                labelRect.pivot = new Vector2(0f, 1f);
                labelRect.anchoredPosition = _slotPositions[i];
                labelRect.sizeDelta = _cellSize;

                Text text = labelGO.AddComponent<Text>();
                text.text = Slots[i].Label;
                text.font = font;
                text.fontSize = 12;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = new Color(1f, 1f, 1f, 0.95f);
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;

                Outline outline = labelGO.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1f, -1f);

                _slotLabels[i] = text;

                _slotAmounts[i] = CreateSlotCornerText(gridRoot, font, "EquipmentSheet_" + Slots[i].Label + "_Amount",
                    _slotPositions[i] + new Vector2(2f, -_cellSize.y + 16f), TextAnchor.MiddleLeft);
                _slotQualities[i] = CreateSlotCornerText(gridRoot, font, "EquipmentSheet_" + Slots[i].Label + "_Quality",
                    _slotPositions[i] + new Vector2(_cellSize.x - 18f, -2f), TextAnchor.MiddleRight);

                CreateSlotDurabilityBar(gridRoot, i);
            }

            CreateSlotHitboxes(gridRoot);

            _panel = panelRect;
            panelGO.SetActive(false);

            if (_log != null)
            {
                _log.LogInfo("Equipment Sheet: created persistent equipment inventory panel.");
            }
        }

        private static void EnsureEquipmentInventory()
        {
            if (_equipmentInventory != null)
            {
                return;
            }

            _equipmentInventory = new Inventory("Equipment Sheet", null, SheetColumns, SheetRows);
            HookEquipmentInventoryChanged();
        }

        private static void HookEquipmentInventoryChanged()
        {
            if (_equipmentInventory == null || _onChangedField == null)
            {
                return;
            }

            Action existing = _onChangedField.GetValue(_equipmentInventory) as Action;
            Action handler = ValidateEquipmentInventory;
            bool alreadyHooked = false;
            if (existing != null)
            {
                Delegate[] list = existing.GetInvocationList();
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] == (Delegate)handler)
                    {
                        alreadyHooked = true;
                        break;
                    }
                }
            }

            if (!alreadyHooked)
            {
                _onChangedField.SetValue(_equipmentInventory, existing + handler);
            }
        }

        private static void UpdateEquipmentGrid(InventoryGui gui, Player player)
        {
            if (_grid == null || _equipmentInventory == null)
            {
                return;
            }

            UpdateSlotVisuals();
        }

        private static Text CreateSlotCornerText(RectTransform parent, Font font, string name, Vector2 position, TextAnchor anchor)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(16f, 14f);

            Text text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 10;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;

            Outline outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        private static void CreateSlotDurabilityBar(RectTransform parent, int slot)
        {
            GameObject backGO = new GameObject("EquipmentSheet_" + Slots[slot].Label + "_DurabilityBack", typeof(RectTransform));
            backGO.transform.SetParent(parent, false);
            RectTransform backRect = backGO.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = _slotPositions[slot] + new Vector2(4f, -_cellSize.y + 7f);
            backRect.sizeDelta = new Vector2(_cellSize.x - 8f, 4f);

            Image back = backGO.AddComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.8f);
            back.raycastTarget = false;
            back.gameObject.SetActive(false);
            _slotDurabilityBacks[slot] = back;

            GameObject barGO = new GameObject("EquipmentSheet_" + Slots[slot].Label + "_DurabilityBar", typeof(RectTransform));
            barGO.transform.SetParent(parent, false);
            RectTransform barRect = barGO.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = backRect.anchoredPosition;
            barRect.sizeDelta = backRect.sizeDelta;

            Image bar = barGO.AddComponent<Image>();
            bar.color = Color.green;
            bar.raycastTarget = false;
            bar.gameObject.SetActive(false);
            _slotDurabilityBars[slot] = bar;
        }

        private static void UpdateSlotVisuals()
        {
            if (_slotLabels == null)
            {
                return;
            }

            for (int i = 0; i < _slotLabels.Length; i++)
            {
                if (_slotLabels[i] == null)
                {
                    continue;
                }

                Vector2i pos = new Vector2i(i % SheetColumns, i / SheetColumns);
                ItemDrop.ItemData item = _equipmentInventory == null ? null : _equipmentInventory.GetItemAt(pos.x, pos.y);
                bool hasItem = item != null && item.m_shared != null;

                _slotLabels[i].text = Slots[i].Label;
                _slotLabels[i].gameObject.SetActive(!hasItem);
                _slotLabels[i].transform.SetAsLastSibling();

                if (_slotIcons != null && i < _slotIcons.Length && _slotIcons[i] != null)
                {
                    _slotIcons[i].sprite = hasItem ? item.GetIcon() : null;
                    _slotIcons[i].gameObject.SetActive(hasItem);
                    _slotIcons[i].transform.SetAsLastSibling();
                }

                if (_slotAmounts != null && i < _slotAmounts.Length && _slotAmounts[i] != null)
                {
                    _slotAmounts[i].text = hasItem && item.m_stack > 1 ? item.m_stack.ToString() : "";
                    _slotAmounts[i].gameObject.SetActive(hasItem && item.m_stack > 1);
                    _slotAmounts[i].transform.SetAsLastSibling();
                }

                if (_slotQualities != null && i < _slotQualities.Length && _slotQualities[i] != null)
                {
                    _slotQualities[i].text = hasItem && item.m_quality > 1 ? item.m_quality.ToString() : "";
                    _slotQualities[i].gameObject.SetActive(hasItem && item.m_quality > 1);
                    _slotQualities[i].transform.SetAsLastSibling();
                }

                UpdateSlotDurability(i, item, hasItem);

                if (_slotTooltips != null && i < _slotTooltips.Length && _slotTooltips[i] != null)
                {
                    if (hasItem && _grid != null)
                    {
                        SetSlotTooltip(item, _slotTooltips[i]);
                    }
                    else
                    {
                        _slotTooltips[i].Set("", "", _grid == null ? null : _grid.m_tooltipAnchor, Vector2.zero);
                    }
                }

                if (_slotHitboxes != null && i < _slotHitboxes.Length && _slotHitboxes[i] != null)
                {
                    _slotHitboxes[i].transform.SetAsLastSibling();
                }
            }
        }

        private static void UpdateSlotDurability(int slot, ItemDrop.ItemData item, bool hasItem)
        {
            bool show = hasItem && item.m_shared != null && item.m_shared.m_useDurability && item.GetMaxDurability() > 0f;
            if (_slotDurabilityBacks != null && slot < _slotDurabilityBacks.Length && _slotDurabilityBacks[slot] != null)
            {
                _slotDurabilityBacks[slot].gameObject.SetActive(show);
                _slotDurabilityBacks[slot].transform.SetAsLastSibling();
            }

            if (_slotDurabilityBars == null || slot >= _slotDurabilityBars.Length || _slotDurabilityBars[slot] == null)
            {
                return;
            }

            Image bar = _slotDurabilityBars[slot];
            bar.gameObject.SetActive(show);
            bar.transform.SetAsLastSibling();
            if (!show)
            {
                return;
            }

            float durability = Mathf.Clamp01(item.m_durability / item.GetMaxDurability());
            RectTransform rect = bar.rectTransform;
            rect.sizeDelta = new Vector2((_cellSize.x - 8f) * durability, 4f);
            bar.color = durability > 0.5f
                ? Color.Lerp(new Color(1f, 0.75f, 0.1f, 1f), new Color(0.1f, 0.85f, 0.15f, 1f), (durability - 0.5f) * 2f)
                : Color.Lerp(new Color(0.85f, 0.1f, 0.05f, 1f), new Color(1f, 0.75f, 0.1f, 1f), durability * 2f);
        }

        private static void CreateSlotHitboxes(RectTransform gridRoot)
        {
            if (gridRoot == null || _slotPositions == null)
            {
                return;
            }

            for (int i = 0; i < Slots.Length; i++)
            {
                int slotIndex = i;
                GameObject hitboxGO = new GameObject("EquipmentSheet_" + Slots[i].Label + "_Hitbox", typeof(RectTransform));
                hitboxGO.transform.SetParent(gridRoot, false);
                hitboxGO.transform.SetAsLastSibling();

                RectTransform rect = hitboxGO.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = _slotPositions[i];
                rect.sizeDelta = _cellSize;

                Image image = hitboxGO.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;

                UIInputHandler handler = hitboxGO.AddComponent<UIInputHandler>();
                handler.m_onLeftDown = delegate(UIInputHandler input)
                {
                    HandleSheetSlotClicked(slotIndex, false);
                };
                handler.m_onRightDown = delegate(UIInputHandler input)
                {
                    HandleSheetSlotClicked(slotIndex, true);
                };

                _slotHitboxes[i] = handler;

                UITooltip tooltip = hitboxGO.AddComponent<UITooltip>();
                ConfigureSlotTooltip(tooltip);
                _slotTooltips[i] = tooltip;
            }
        }

        private static void ConfigureSlotTooltip(UITooltip tooltip)
        {
            if (tooltip == null)
            {
                return;
            }

            InventoryGui gui = InventoryGui.instance;
            UITooltip source = gui != null && gui.m_playerGrid != null && gui.m_playerGrid.m_elementPrefab != null
                ? gui.m_playerGrid.m_elementPrefab.GetComponentInChildren<UITooltip>(true)
                : null;

            if (source != null)
            {
                tooltip.m_tooltipPrefab = source.m_tooltipPrefab;
                tooltip.m_gamepadFocusObject = source.m_gamepadFocusObject;
            }
        }

        private static void SetSlotTooltip(ItemDrop.ItemData item, UITooltip tooltip)
        {
            if (item == null || tooltip == null)
            {
                return;
            }

            if (_createItemTooltipMethod != null && _grid != null)
            {
                _createItemTooltipMethod.Invoke(_grid, new object[] { item, tooltip });
                return;
            }

            string topic = item.m_shared == null ? "" : item.m_shared.m_name;
            tooltip.Set(topic, item.GetTooltip(-1), _grid == null ? null : _grid.m_tooltipAnchor, Vector2.zero);
        }

        private static void HandleSheetSlotClicked(int slotIndex, bool rightClick)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Length || _equipmentInventory == null)
            {
                return;
            }

            InventoryGui gui = InventoryGui.instance;
            if (gui == null)
            {
                return;
            }

            Vector2i pos = new Vector2i(slotIndex % SheetColumns, slotIndex / SheetColumns);
            ItemDrop.ItemData item = _equipmentInventory.GetItemAt(pos.x, pos.y);

            if (IsFoodSlot(slotIndex) && item != null && rightClick && !HasActiveDrag(gui))
            {
                TryConsumeFoodSlot(slotIndex, item);
                return;
            }

            if (IsEquipmentSlot(slotIndex) && item != null && rightClick && !HasActiveDrag(gui))
            {
                MoveEquipmentItemToPlayerBag(item);
                return;
            }

            HandleEquipmentGridSelected(gui, _grid, item, pos, InventoryGrid.Modifier.Select);
        }

        private static bool HasActiveDrag(InventoryGui gui)
        {
            return gui != null && _dragItemField != null && _dragItemField.GetValue(gui) is ItemDrop.ItemData;
        }

        private static void TryConsumeFoodSlot(int slotIndex, ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (player == null || _equipmentInventory == null || !IsFoodSlot(slotIndex) || !MatchesSlot(item, slotIndex))
            {
                return;
            }

            player.UseItem(_equipmentInventory, item, fromInventoryGui: true);
            RefreshEquipmentInventory();
            SaveEquipmentInventory(player);
        }

        private static void ValidateEquipmentInventory()
        {
            if (_loadingInventory || _equipmentInventory == null)
            {
                return;
            }

            bool changed = false;
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(_equipmentInventory.GetAllItems());

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null)
                {
                    continue;
                }

                int slot = GetSlotIndex(item.m_gridPos);
                if (slot >= 0 && MatchesSlot(item, slot))
                {
                    SetEquippedFlag(item, IsEquipmentSlot(slot));
                    continue;
                }

                MoveItemBackToPlayerBag(item);
                changed = true;
            }

            if (changed)
            {
                RefreshEquipmentInventory();
                ShowWrongItemTypeMessage();
            }

            SyncCharacterEquipment(Player.m_localPlayer);
            SaveEquipmentInventory(Player.m_localPlayer);
        }

        internal static void QueueAutoRouteEquippedItem(Humanoid humanoid, ItemDrop.ItemData item, bool equipped)
        {
            Player player = humanoid as Player;
            if (!equipped || player == null || player != Player.m_localPlayer || item == null)
            {
                return;
            }

            Inventory playerInventory = player.GetInventory();
            if (item == _manualSheetDragItem && playerInventory != null && playerInventory.ContainsItem(item))
            {
                _manualSheetDragItem = null;
                PendingAutoRouteItems.Remove(item);
                player.UnequipItem(item, false);
                return;
            }

            if (player != _loadedPlayer || _enabled == null || !_enabled.Value || GetEquipmentSlotForItem(item) < 0)
            {
                return;
            }

            if (playerInventory == null || !playerInventory.ContainsItem(item) || !player.IsItemEquiped(item))
            {
                return;
            }

            if (!PendingAutoRouteItems.Contains(item))
            {
                PendingAutoRouteItems.Add(item);
            }
        }

        private static void ProcessPendingAutoRouteItems(Player player)
        {
            if (PendingAutoRouteItems.Count == 0)
            {
                return;
            }

            List<ItemDrop.ItemData> pending = new List<ItemDrop.ItemData>(PendingAutoRouteItems);
            PendingAutoRouteItems.Clear();
            for (int i = 0; i < pending.Count; i++)
            {
                MoveEquippedBagItemToSheet(player, pending[i]);
            }
        }

        private static void MoveEquippedBagItemToSheet(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null || _equipmentInventory == null || !player.IsItemEquiped(item))
            {
                return;
            }

            int slot = GetEquipmentSlotForItem(item);
            Inventory playerInventory = player.GetInventory();
            List<ItemDrop.ItemData> playerItems = GetInventoryItems(playerInventory);
            List<ItemDrop.ItemData> sheetItems = GetInventoryItems(_equipmentInventory);
            if (slot < 0 || playerInventory == null || playerItems == null || sheetItems == null || !playerItems.Contains(item))
            {
                return;
            }

            Vector2i sheetPosition = new Vector2i(slot % SheetColumns, slot / SheetColumns);
            Vector2i bagPosition = item.m_gridPos;
            if (playerInventory.GetItemAt(bagPosition.x, bagPosition.y) != item)
            {
                return;
            }

            ItemDrop.ItemData previous = _equipmentInventory.GetItemAt(sheetPosition.x, sheetPosition.y);
            if (previous == item)
            {
                return;
            }

            playerItems.Remove(item);
            if (previous != null)
            {
                sheetItems.Remove(previous);
                previous.m_gridPos = bagPosition;
                SetEquippedFlag(previous, false);
                playerItems.Add(previous);
            }

            item.m_gridPos = sheetPosition;
            SetEquippedFlag(item, true);
            sheetItems.Add(item);

            bool swapValid = _equipmentInventory.GetItemAt(sheetPosition.x, sheetPosition.y) == item
                && (previous == null || playerInventory.GetItemAt(bagPosition.x, bagPosition.y) == previous);
            if (!swapValid)
            {
                sheetItems.Remove(item);
                item.m_gridPos = bagPosition;
                SetEquippedFlag(item, true);
                playerItems.Add(item);

                if (previous != null)
                {
                    playerItems.Remove(previous);
                    previous.m_gridPos = sheetPosition;
                    SetEquippedFlag(previous, true);
                    sheetItems.Add(previous);
                }

                return;
            }

            RefreshEquipmentInventory();
            if (_inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(playerInventory);
            }

            SaveEquipmentInventory(player);
        }

        private static void MoveEquipmentItemToPlayerBag(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            Inventory playerInventory = player == null ? null : player.GetInventory();
            List<ItemDrop.ItemData> playerItems = GetInventoryItems(playerInventory);
            List<ItemDrop.ItemData> sheetItems = GetInventoryItems(_equipmentInventory);
            if (player == null || item == null || playerInventory == null || playerItems == null || sheetItems == null
                || !sheetItems.Contains(item))
            {
                return;
            }

            Vector2i bagPosition = FindFirstEmptyPosition(playerInventory);
            if (bagPosition.x < 0)
            {
                player.Message(MessageHud.MessageType.Center, "$inventory_full");
                return;
            }

            Vector2i sheetPosition = item.m_gridPos;
            SavePendingTransfer(player, item);
            player.UnequipItem(item, false);
            sheetItems.Remove(item);
            item.m_gridPos = bagPosition;
            SetEquippedFlag(item, false);
            playerItems.Add(item);

            bool moved = playerInventory.GetItemAt(bagPosition.x, bagPosition.y) == item && !_equipmentInventory.ContainsItem(item);
            if (!moved)
            {
                playerItems.Remove(item);
                item.m_gridPos = sheetPosition;
                SetEquippedFlag(item, true);
                sheetItems.Add(item);
                SyncCharacterEquipment(player);
                ClearPendingTransfer(player);
                return;
            }

            RefreshEquipmentInventory();
            if (_inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(playerInventory);
            }

            SaveEquipmentInventory(player);
            ClearPendingTransfer(player);
        }

        private static Vector2i FindFirstEmptyPosition(Inventory inventory)
        {
            if (inventory == null)
            {
                return new Vector2i(-1, -1);
            }

            int width = inventory.GetWidth();
            int height = inventory.GetHeight();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (inventory.GetItemAt(x, y) == null)
                    {
                        return new Vector2i(x, y);
                    }
                }
            }

            return new Vector2i(-1, -1);
        }

        private static int GetEquipmentSlotForItem(ItemDrop.ItemData item)
        {
            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                if (MatchesSlot(item, i))
                {
                    return i;
                }
            }

            return -1;
        }

        private static FieldInfo GetHumanoidFieldForType(ItemDrop.ItemData.ItemType type)
        {
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                    return _humanoidHelmetField;
                case ItemDrop.ItemData.ItemType.Chest:
                    return _humanoidChestField;
                case ItemDrop.ItemData.ItemType.Legs:
                    return _humanoidLegField;
                case ItemDrop.ItemData.ItemType.Trinket:
                    return _humanoidTrinketField;
                case ItemDrop.ItemData.ItemType.Shoulder:
                    return _humanoidShoulderField;
                case ItemDrop.ItemData.ItemType.Utility:
                    return _humanoidUtilityField;
                default:
                    return null;
            }
        }

        // Valheim never re-derives worn gear from Inventory.GetEquippedItems(); Humanoid caches the
        // equipped item directly in fields like m_chestItem, and armor/visuals/status effects read
        // those fields only. Humanoid.EquipItem() is the normal way to populate them, but it hard-
        // requires the item to be sitting in the player's own bag inventory, which equipment-sheet
        // items intentionally are not. This reconciles those cached fields by hand so gear placed in
        // the sheet actually equips (armor value, visuals, set bonuses) instead of just sitting there
        // looking equipped in this mod's own UI.
        private static void SyncCharacterEquipment(Player player)
        {
            if (player == null || _equipmentInventory == null || _setupEquipmentMethod == null)
            {
                return;
            }

            bool needsRefresh = false;

            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                FieldInfo field = GetHumanoidFieldForType(Slots[i].Type);
                if (field == null)
                {
                    continue;
                }

                Vector2i pos = new Vector2i(i % SheetColumns, i / SheetColumns);
                ItemDrop.ItemData desired = _equipmentInventory.GetItemAt(pos.x, pos.y);
                if (desired != null && (desired.m_shared == null || !MatchesSlot(desired, i)))
                {
                    desired = null;
                }

                ItemDrop.ItemData current = field.GetValue(player) as ItemDrop.ItemData;
                if (current == desired)
                {
                    continue;
                }

                // If the slot's sheet item is empty and whatever the character currently has
                // equipped there did not come from the sheet, it is a normal vanilla-equipped
                // bag item (e.g. the player right-clicked it in their bag) - leave it alone.
                bool currentIsOurs = current != null && _equipmentInventory.ContainsItem(current);
                if (desired == null && !currentIsOurs)
                {
                    continue;
                }

                if (current != null)
                {
                    player.UnequipItem(current, false);
                }

                if (desired != null)
                {
                    field.SetValue(player, desired);
                    SetEquippedFlag(desired, true);
                }

                needsRefresh = true;
            }

            if (needsRefresh)
            {
                _setupEquipmentMethod.Invoke(player, null);
            }
        }

        internal static bool IsEquipmentGrid(InventoryGrid grid)
        {
            return grid != null && _grid != null && grid == _grid;
        }

        // InventoryGui.UpdateContainer() runs every frame and has a vanilla safety net: if a drag is
        // active, no container window is open, and the drag's source inventory isn't the player's own
        // bag, it assumes the drag is orphaned (its container just closed) and cancels it. The
        // equipment sheet is a third kind of inventory vanilla doesn't know about, so it always fails
        // that check - every drag started from a sheet slot gets silently cancelled within the same
        // frame it starts, before the ghost icon can ever be seen. This brackets that one check: swap
        // m_dragInventory to the player's own bag just long enough for UpdateContainer to run past it,
        // then restore the real value immediately after (see the Harmony patch below).
        internal static bool BeginContainerUpdateGuard(InventoryGui gui)
        {
            if (gui == null || _dragInventoryField == null || _currentContainerField == null)
            {
                return false;
            }

            Inventory dragInventory = _dragInventoryField.GetValue(gui) as Inventory;
            if (dragInventory == null || dragInventory != _equipmentInventory)
            {
                return false;
            }

            Container currentContainer = _currentContainerField.GetValue(gui) as Container;
            if (currentContainer != null)
            {
                return false;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            _dragInventoryField.SetValue(gui, player.GetInventory());
            return true;
        }

        internal static void EndContainerUpdateGuard(InventoryGui gui, bool didSwap)
        {
            if (!didSwap || gui == null || _dragInventoryField == null)
            {
                return;
            }

            _dragInventoryField.SetValue(gui, _equipmentInventory);
        }

        internal static bool TryDropIntoEquipmentSlot(Inventory fromInventory, ItemDrop.ItemData item, int amount, Vector2i pos)
        {
            if (_equipmentInventory == null || fromInventory == null || item == null || item.m_shared == null)
            {
                return false;
            }

            // Picking an item up out of a slot (see HandleEquipmentGridSelected) does not remove it
            // from _equipmentInventory - only vanilla InventoryGui's drag bookkeeping changes - so the
            // slot the item came from still reports as occupied by that same item. Vanilla's own
            // InventoryGrid.DropItem treats "dropped back where it started" as a trivial success; without
            // the same check here, clicking that slot again just sees it occupied and always fails,
            // which means ClearDragItem never runs and the drag gets stuck forever on that slot.
            if (_equipmentInventory.GetItemAt(pos.x, pos.y) == item)
            {
                return true;
            }

            int slot = GetSlotIndex(pos);
            if (slot < 0 || !MatchesSlot(item, slot))
            {
                ShowWrongItemTypeMessage();
                return false;
            }

            if (IsFoodSlot(slot))
            {
                return TryDropIntoFoodSlot(fromInventory, item, amount, pos);
            }

            if (_equipmentInventory.GetItemAt(pos.x, pos.y) != null)
            {
                return false;
            }

            int stack = Mathf.Clamp(amount <= 0 ? item.m_stack : amount, 1, item.m_stack);
            if (stack != item.m_stack)
            {
                ShowWrongItemTypeMessage();
                return false;
            }

            if (fromInventory == _equipmentInventory)
            {
                item.m_gridPos = pos;
                SetEquippedFlag(item, true);
                RefreshEquipmentInventory();
                SaveEquipmentInventory(Player.m_localPlayer);
                return true;
            }

            Player player = Player.m_localPlayer;
            SavePendingTransfer(player, item);

            List<ItemDrop.ItemData> sourceItems = GetInventoryItems(fromInventory);
            List<ItemDrop.ItemData> equipmentItems = GetInventoryItems(_equipmentInventory);
            if (sourceItems == null || equipmentItems == null || !sourceItems.Contains(item))
            {
                ClearPendingTransfer(player);
                return false;
            }

            Vector2i originalPos = item.m_gridPos;
            sourceItems.Remove(item);
            item.m_gridPos = pos;
            SetEquippedFlag(item, true);
            equipmentItems.Add(item);

            if (_equipmentInventory.GetItemAt(pos.x, pos.y) != item)
            {
                equipmentItems.Remove(item);
                item.m_gridPos = originalPos;
                SetEquippedFlag(item, false);
                sourceItems.Add(item);

                RestorePendingTransfer(player);
                return false;
            }

            RefreshEquipmentInventory();
            if (_inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(fromInventory);
            }

            SaveEquipmentInventory(Player.m_localPlayer);
            ClearPendingTransfer(player);
            return true;
        }

        private static bool TryDropIntoFoodSlot(Inventory fromInventory, ItemDrop.ItemData item, int amount, Vector2i pos)
        {
            if (_equipmentInventory == null || fromInventory == null || item == null || !IsFoodItem(item))
            {
                return false;
            }

            int stack = Mathf.Clamp(amount <= 0 ? item.m_stack : amount, 1, item.m_stack);
            ItemDrop.ItemData target = _equipmentInventory.GetItemAt(pos.x, pos.y);
            if (target == item)
            {
                return true;
            }

            if (fromInventory == _equipmentInventory)
            {
                return TryMoveFoodInsideSheet(item, stack, target, pos);
            }

            int beforeStack = item.m_stack;
            bool movedAll = _equipmentInventory.MoveItemToThis(fromInventory, item, stack, pos.x, pos.y);
            bool movedAny = item.m_stack != beforeStack || movedAll;
            if (!movedAny)
            {
                return false;
            }

            SetEquippedFlag(item, false);
            RefreshEquipmentInventory();
            if (_inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(fromInventory);
            }

            SaveEquipmentInventory(Player.m_localPlayer);
            return true;
        }

        private static bool TryMoveFoodInsideSheet(ItemDrop.ItemData item, int amount, ItemDrop.ItemData target, Vector2i pos)
        {
            List<ItemDrop.ItemData> sheetItems = GetInventoryItems(_equipmentInventory);
            if (sheetItems == null || item == null || !sheetItems.Contains(item))
            {
                return false;
            }

            if (target == null)
            {
                item.m_gridPos = pos;
                SetEquippedFlag(item, false);
                RefreshEquipmentInventory();
                SaveEquipmentInventory(Player.m_localPlayer);
                return true;
            }

            if (!CanStackFood(target, item))
            {
                return false;
            }

            int freeSpace = target.m_shared.m_maxStackSize - target.m_stack;
            if (freeSpace <= 0)
            {
                return false;
            }

            int moved = Mathf.Min(Mathf.Clamp(amount, 1, item.m_stack), freeSpace);
            target.m_stack += moved;
            item.m_stack -= moved;
            SetEquippedFlag(target, false);
            SetEquippedFlag(item, false);

            if (item.m_stack <= 0)
            {
                sheetItems.Remove(item);
            }

            RefreshEquipmentInventory();
            SaveEquipmentInventory(Player.m_localPlayer);
            return true;
        }

        private static bool CanStackFood(ItemDrop.ItemData target, ItemDrop.ItemData item)
        {
            if (target == null || item == null || target.m_shared == null || item.m_shared == null)
            {
                return false;
            }

            return target.m_shared.m_name == item.m_shared.m_name
                && target.m_quality == item.m_quality
                && target.m_worldLevel == item.m_worldLevel
                && target.m_shared.m_maxStackSize > 1;
        }

        private static void HandleEquipmentGridSelected(InventoryGui gui, InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos, InventoryGrid.Modifier mod)
        {
            if (!IsEquipmentGrid(grid) || gui == null)
            {
                return;
            }

            ItemDrop.ItemData dragItem = _dragItemField == null ? null : _dragItemField.GetValue(gui) as ItemDrop.ItemData;
            Inventory dragInventory = _dragInventoryField == null ? null : _dragInventoryField.GetValue(gui) as Inventory;
            int dragAmount = _dragAmountField == null ? 0 : (int)_dragAmountField.GetValue(gui);

            if (dragItem != null)
            {
                if (TryDropIntoEquipmentSlot(dragInventory, dragItem, dragAmount, pos))
                {
                    ClearDragItem(gui);
                }

                return;
            }

            if (item != null && _setupDragItemMethod != null)
            {
                SetEquippedFlag(item, false);
                _manualSheetDragItem = item;
                _setupDragItemMethod.Invoke(gui, new object[] { item, _equipmentInventory, item.m_stack });
            }
        }

        private static void ClearDragItem(InventoryGui gui)
        {
            if (gui == null)
            {
                return;
            }

            ItemDrop.ItemData dragItem = _dragItemField == null ? null : _dragItemField.GetValue(gui) as ItemDrop.ItemData;
            if (_manualSheetDragItem == dragItem)
            {
                _manualSheetDragItem = null;
            }

            GameObject dragGo = _dragGoField == null ? null : _dragGoField.GetValue(gui) as GameObject;
            if (dragGo != null)
            {
                UnityEngine.Object.Destroy(dragGo);
            }

            if (_dragGoField != null)
            {
                _dragGoField.SetValue(gui, null);
            }

            if (_dragItemField != null)
            {
                _dragItemField.SetValue(gui, null);
            }

            if (_dragInventoryField != null)
            {
                _dragInventoryField.SetValue(gui, null);
            }

            if (_dragAmountField != null)
            {
                _dragAmountField.SetValue(gui, 0);
            }
        }

        private static int GetSlotIndex(Vector2i pos)
        {
            if (pos.x < 0 || pos.x >= SheetColumns || pos.y < 0 || pos.y >= SheetRows)
            {
                return -1;
            }

            int slot = pos.y * SheetColumns + pos.x;
            return slot >= 0 && slot < Slots.Length ? slot : -1;
        }

        private static bool IsEquipmentSlot(int slot)
        {
            return slot >= 0 && slot < EquipmentSlotCount && slot < Slots.Length
                && Slots[slot].Kind == SlotKind.Equipment;
        }

        private static bool IsFoodSlot(int slot)
        {
            return slot >= EquipmentSlotCount && slot < Slots.Length
                && Slots[slot].Kind == SlotKind.Food;
        }

        private static bool MatchesSlot(ItemDrop.ItemData item, int slot)
        {
            if (item == null || item.m_shared == null || slot < 0 || slot >= Slots.Length)
            {
                return false;
            }

            if (IsFoodSlot(slot))
            {
                return IsFoodItem(item);
            }

            if (!IsEquipmentSlot(slot))
            {
                return false;
            }

            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            if (IsExplicitlyExcludedEquipmentType(type))
            {
                return false;
            }

            return type == Slots[slot].Type;
        }

        private static bool IsFoodItem(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null)
            {
                return false;
            }

            return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable
                && (item.m_shared.m_food > 0f
                    || item.m_shared.m_foodStamina > 0f
                    || item.m_shared.m_foodEitr > 0f
                    || item.m_shared.m_consumeStatusEffect != null);
        }

        private static bool IsExplicitlyExcludedEquipmentType(ItemDrop.ItemData.ItemType type)
        {
            if (type == ItemDrop.ItemData.ItemType.Bow
                || type == ItemDrop.ItemData.ItemType.OneHandedWeapon
                || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon
                || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                || type == ItemDrop.ItemData.ItemType.Attach_Atgeir
                || type == ItemDrop.ItemData.ItemType.Shield
                || type == ItemDrop.ItemData.ItemType.Tool
                || type == ItemDrop.ItemData.ItemType.Torch)
            {
                return true;
            }

            string name = type.ToString();
            return name.IndexOf("Bow", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Ranged", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void MoveItemBackToPlayerBag(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            Inventory playerInventory = player == null ? null : player.GetInventory();
            if (item == null || playerInventory == null || _equipmentInventory == null)
            {
                return;
            }

            if (!_equipmentInventory.ContainsItem(item))
            {
                return;
            }

            SetEquippedFlag(item, false);

            // Use the real Inventory.AddItem so it lands on a free cell. Writing item.m_gridPos
            // straight into the player's bag list (the previous approach) reused the equipment
            // sheet's own (0,0)-(2,1) coordinates, which sit inside the bag's own grid and can
            // silently overlap and hide whatever bag item already occupies that cell.
            if (!playerInventory.AddItem(item))
            {
                int slot = GetSlotIndex(item.m_gridPos);
                SetEquippedFlag(item, slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot));
                if (_log != null)
                {
                    _log.LogWarning("Equipment Sheet: invalid item '" + GetItemDisplayName(item)
                        + "' is in an equipment slot, but the player bag is full.");
                }
                return;
            }

            _equipmentInventory.RemoveItem(item);

            if (_log != null)
            {
                _log.LogInfo("Equipment Sheet: moved invalid item '" + GetItemDisplayName(item)
                    + "' back to the player bag.");
            }
        }

        internal static void SaveEquipmentInventory(Player player)
        {
            if (player == null || _equipmentInventory == null)
            {
                return;
            }

            Dictionary<string, string> customData = GetCustomData(player);
            if (customData == null)
            {
                return;
            }

            ZPackage package = new ZPackage();
            _equipmentInventory.Save(package);
            customData[CustomDataKey] = package.GetBase64();
        }

        private static void SavePendingTransfer(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null)
            {
                return;
            }

            Dictionary<string, string> customData = GetCustomData(player);
            if (customData == null)
            {
                return;
            }

            Inventory pending = new Inventory("EquipmentSheetPending", null, 1, 1);
            ItemDrop.ItemData clone = item.Clone();
            clone.m_gridPos = new Vector2i(0, 0);
            pending.AddItem(clone, clone.m_gridPos);

            ZPackage package = new ZPackage();
            pending.Save(package);
            customData[PendingTransferKey] = package.GetBase64();
        }

        private static void ClearPendingTransfer(Player player)
        {
            Dictionary<string, string> customData = GetCustomData(player);
            if (customData != null)
            {
                customData.Remove(PendingTransferKey);
            }
        }

        private static void RestorePendingTransfer(Player player)
        {
            if (player == null)
            {
                return;
            }

            Dictionary<string, string> customData = GetCustomData(player);
            string encoded;
            if (customData == null || !customData.TryGetValue(PendingTransferKey, out encoded) || string.IsNullOrEmpty(encoded))
            {
                return;
            }

            Inventory playerInventory = player.GetInventory();
            if (playerInventory == null)
            {
                return;
            }

            try
            {
                Inventory pending = new Inventory("EquipmentSheetPending", null, 1, 1);
                ZPackage package = new ZPackage();
                package.Load(Convert.FromBase64String(encoded));
                pending.Load(package);

                List<ItemDrop.ItemData> items = pending.GetAllItems();
                for (int i = 0; i < items.Count; i++)
                {
                    ItemDrop.ItemData item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    ItemDrop.ItemData clone = item.Clone();
                    SetEquippedFlag(clone, false);
                    playerInventory.AddItem(clone);
                }

                ClearPendingTransfer(player);
                if (_inventoryChangedMethod != null)
                {
                    NotifyInventoryChanged(playerInventory);
                }

                if (_log != null)
                {
                    _log.LogWarning("Equipment Sheet: restored an interrupted equipment transfer back to the player bag.");
                }
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.LogWarning("Equipment Sheet: failed to restore pending transfer: " + ex.Message);
                }
            }
        }

        internal static void LoadEquipmentInventory(Player player)
        {
            EnsureEquipmentInventory();

            Dictionary<string, string> customData = GetCustomData(player);
            string encoded;
            if (customData == null || !customData.TryGetValue(CustomDataKey, out encoded) || string.IsNullOrEmpty(encoded))
            {
                ClearEquipmentInventory(player, false);
                return;
            }

            try
            {
                _loadingInventory = true;
                ZPackage package = new ZPackage();
                package.Load(Convert.FromBase64String(encoded));

                _equipmentInventory.RemoveAll();
                _equipmentInventory.Load(package);
                MarkEquipmentItemsEquipped();
                RefreshEquipmentInventory();

                if (_log != null)
                {
                    _log.LogInfo("Equipment Sheet: loaded persistent equipment inventory.");
                }
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.LogWarning("Equipment Sheet: failed to load persistent equipment inventory: " + ex.Message);
                }
            }
            finally
            {
                _loadingInventory = false;
            }

            // Must run after _loadingInventory is cleared and after the vanilla Player.Load() body
            // (including its own EquipInventoryItems() bag scan) has already finished, otherwise the
            // sheet's items would still be un-loaded at the moment the character's gear visuals/stats
            // get set up.
            SyncCharacterEquipment(player);
        }

        internal static void MoveSheetItemsToPlayerInventoryForDeath(Player player)
        {
            if (player == null || _equipmentInventory == null)
            {
                return;
            }

            Inventory playerInventory = player.GetInventory();
            List<ItemDrop.ItemData> playerItems = GetInventoryItems(playerInventory);
            List<ItemDrop.ItemData> equipmentItems = GetInventoryItems(_equipmentInventory);
            if (playerInventory == null || playerItems == null || equipmentItems == null || equipmentItems.Count == 0)
            {
                ClearEquipmentCustomData(player);
                return;
            }

            List<ItemDrop.ItemData> moved = new List<ItemDrop.ItemData>(equipmentItems);
            for (int i = 0; i < moved.Count; i++)
            {
                ItemDrop.ItemData item = moved[i];
                if (item == null)
                {
                    continue;
                }

                int slot = GetSlotIndex(item.m_gridPos);
                SetEquippedFlag(item, slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot));
                if (!playerItems.Contains(item))
                {
                    playerItems.Add(item);
                }

                equipmentItems.Remove(item);
            }

            ClearEquipmentCustomData(player);
            RefreshEquipmentInventory();
            if (_inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(playerInventory);
            }

            if (_log != null)
            {
                _log.LogInfo("Equipment Sheet: moved sheet items into the vanilla death-drop inventory.");
            }
        }

        private static void ClearEquipmentInventory(Player player, bool clearSavedData)
        {
            if (_equipmentInventory != null && _equipmentInventory.NrOfItems() > 0)
            {
                List<ItemDrop.ItemData> oldItems = new List<ItemDrop.ItemData>(_equipmentInventory.GetAllItems());
                _loadingInventory = true;
                try
                {
                    _equipmentInventory.RemoveAll();
                }
                finally
                {
                    _loadingInventory = false;
                }

                RefreshEquipmentInventory();
                ClearCharacterEquipmentReferences(player, oldItems);
            }

            if (clearSavedData)
            {
                ClearEquipmentCustomData(player);
            }
        }

        private static void ClearCharacterEquipmentReferences(Player player, List<ItemDrop.ItemData> oldItems)
        {
            if (player == null || oldItems == null || oldItems.Count == 0 || _setupEquipmentMethod == null)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < EquipmentSlotCount; i++)
            {
                FieldInfo field = GetHumanoidFieldForType(Slots[i].Type);
                if (field == null)
                {
                    continue;
                }

                ItemDrop.ItemData current = field.GetValue(player) as ItemDrop.ItemData;
                if (current != null && oldItems.Contains(current))
                {
                    field.SetValue(player, null);
                    SetEquippedFlag(current, false);
                    changed = true;
                }
            }

            if (changed)
            {
                _setupEquipmentMethod.Invoke(player, null);
            }
        }

        private static void ClearEquipmentCustomData(Player player)
        {
            Dictionary<string, string> customData = GetCustomData(player);
            if (customData != null)
            {
                customData.Remove(CustomDataKey);
                customData.Remove(PendingTransferKey);
            }
        }

        private static void MarkEquipmentItemsEquipped()
        {
            if (_equipmentInventory == null)
            {
                return;
            }

            List<ItemDrop.ItemData> items = _equipmentInventory.GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                int slot = item == null ? -1 : GetSlotIndex(item.m_gridPos);
                SetEquippedFlag(item, slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot));
            }
        }

        private static void RefreshEquipmentInventory()
        {
            if (_equipmentInventory != null && _inventoryChangedMethod != null)
            {
                NotifyInventoryChanged(_equipmentInventory);
            }
        }

        private static void NotifyInventoryChanged(Inventory inventory)
        {
            if (_inventoryChangedMethod != null && inventory != null)
            {
                _inventoryChangedMethod.Invoke(inventory, new object[] { true, false });
            }
        }

        private static void SetEquippedFlag(ItemDrop.ItemData item, bool equipped)
        {
            if (_itemEquippedField != null && item != null)
            {
                _itemEquippedField.SetValue(item, equipped);
            }
        }

        private static List<ItemDrop.ItemData> GetInventoryItems(Inventory inventory)
        {
            return _inventoryItemsField == null || inventory == null
                ? null
                : _inventoryItemsField.GetValue(inventory) as List<ItemDrop.ItemData>;
        }

        internal static float GetEquipmentWeight()
        {
            return _equipmentInventory == null ? 0f : _equipmentInventory.GetTotalWeight();
        }

        internal static bool BeginUpgradeRecipeScan(InventoryGui gui)
        {
            bool active = gui != null && _enabled != null && _enabled.Value && gui.InUpradeTab();
            if (active)
            {
                _upgradeRecipeScanDepth++;
            }

            return active;
        }

        internal static void EndUpgradeRecipeScan(bool active)
        {
            if (active && _upgradeRecipeScanDepth > 0)
            {
                _upgradeRecipeScanDepth--;
            }
        }

        internal static void AddSheetUpgradeItems(Inventory inventory, string name, List<ItemDrop.ItemData> items)
        {
            Player player = Player.m_localPlayer;
            if (_upgradeRecipeScanDepth <= 0 || _equipmentInventory == null || player == null
                || inventory != player.GetInventory() || string.IsNullOrEmpty(name) || items == null)
            {
                return;
            }

            List<ItemDrop.ItemData> sheetItems = _equipmentInventory.GetAllItems();
            for (int i = 0; i < sheetItems.Count; i++)
            {
                ItemDrop.ItemData item = sheetItems[i];
                int slot = item == null ? -1 : GetSlotIndex(item.m_gridPos);
                if (slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot)
                    && item.m_shared.m_name == name && item.m_worldLevel >= Game.m_worldLevel
                    && !items.Contains(item))
                {
                    items.Add(item);
                }
            }
        }

        internal static bool BeginSheetUpgrade(InventoryGui gui, Player player)
        {
            if (gui == null || player == null || _equipmentInventory == null || _craftUpgradeItemField == null)
            {
                return false;
            }

            ItemDrop.ItemData item = _craftUpgradeItemField.GetValue(gui) as ItemDrop.ItemData;
            int slot = item == null ? -1 : GetSlotIndex(item.m_gridPos);
            if (slot < 0 || !IsEquipmentSlot(slot) || !MatchesSlot(item, slot) || !_equipmentInventory.ContainsItem(item))
            {
                return false;
            }

            Inventory playerInventory = player.GetInventory();
            if (playerInventory == null)
            {
                return false;
            }

            if (_sheetUpgradeTransaction != null)
            {
                EndSheetUpgrade(new InvalidOperationException("A previous equipment upgrade transaction was still active."));
            }

            SavePendingTransfer(player, item);
            _sheetUpgradeTransaction = new SheetUpgradeTransaction
            {
                Player = player,
                PlayerInventory = playerInventory,
                OriginalItem = item,
                SheetPosition = item.m_gridPos
            };
            return true;
        }

        internal static void EndSheetUpgrade(Exception exception)
        {
            SheetUpgradeTransaction transaction = _sheetUpgradeTransaction;
            _sheetUpgradeTransaction = null;
            if (transaction == null)
            {
                return;
            }

            bool completed = exception == null && transaction.OriginalRemoved && transaction.ReplacementItem != null;
            if (!completed && transaction.OriginalRemoved)
            {
                List<ItemDrop.ItemData> sheetItems = GetInventoryItems(_equipmentInventory);
                if (sheetItems != null)
                {
                    if (transaction.ReplacementItem != null)
                    {
                        sheetItems.Remove(transaction.ReplacementItem);
                    }

                    if (!sheetItems.Contains(transaction.OriginalItem))
                    {
                        transaction.OriginalItem.m_gridPos = transaction.SheetPosition;
                        SetEquippedFlag(transaction.OriginalItem, true);
                        sheetItems.Add(transaction.OriginalItem);
                    }
                }

                if (_log != null)
                {
                    _log.LogWarning("Equipment Sheet: restored '" + GetItemDisplayName(transaction.OriginalItem)
                        + "' after its upgrade did not complete.");
                }
            }

            if (completed)
            {
                SetEquippedFlag(transaction.ReplacementItem, true);
            }

            RefreshEquipmentInventory();
            SyncCharacterEquipment(transaction.Player);
            SaveEquipmentInventory(transaction.Player);
            ClearPendingTransfer(transaction.Player);
        }

        internal static void IncludeSheetUpgradeItem(Inventory inventory, ItemDrop.ItemData item, ref bool result)
        {
            SheetUpgradeTransaction transaction = _sheetUpgradeTransaction;
            if (!result && transaction != null && inventory == transaction.PlayerInventory && item == transaction.OriginalItem
                && _equipmentInventory != null && _equipmentInventory.ContainsItem(item))
            {
                result = true;
            }
        }

        internal static bool RemoveSheetUpgradeItem(Inventory inventory, ItemDrop.ItemData item, ref bool result)
        {
            SheetUpgradeTransaction transaction = _sheetUpgradeTransaction;
            if (transaction == null || inventory != transaction.PlayerInventory || item != transaction.OriginalItem)
            {
                return true;
            }

            result = _equipmentInventory != null && _equipmentInventory.RemoveItem(item);
            transaction.OriginalRemoved = result;
            return false;
        }

        internal static bool AddUpgradedSheetItem(Inventory inventory, string name, int stack, int quality, int variant,
            long crafterID, string crafterName, bool cheated, bool pickedUp, ref ItemDrop.ItemData result)
        {
            SheetUpgradeTransaction transaction = _sheetUpgradeTransaction;
            if (transaction == null || !transaction.OriginalRemoved || inventory != transaction.PlayerInventory
                || transaction.OriginalItem == null || transaction.OriginalItem.m_dropPrefab == null
                || name != transaction.OriginalItem.m_dropPrefab.name
                || quality != transaction.OriginalItem.m_quality + 1
                || variant != transaction.OriginalItem.m_variant
                || transaction.SheetPosition.x < 0 || transaction.SheetPosition.y < 0)
            {
                return true;
            }

            result = _equipmentInventory.AddItem(name, stack, quality, variant, crafterID, crafterName,
                transaction.SheetPosition, cheated, pickedUp, true);
            transaction.ReplacementItem = result;
            return false;
        }

        internal static void AddEquippedItems(List<ItemDrop.ItemData> items)
        {
            if (_equipmentInventory == null || items == null)
            {
                return;
            }

            List<ItemDrop.ItemData> equipmentItems = _equipmentInventory.GetAllItems();
            for (int i = 0; i < equipmentItems.Count; i++)
            {
                ItemDrop.ItemData item = equipmentItems[i];
                int slot = item == null ? -1 : GetSlotIndex(item.m_gridPos);
                if (slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot) && !items.Contains(item))
                {
                    SetEquippedFlag(item, true);
                    items.Add(item);
                }
            }
        }

        // Inventory.GetWornItems() only returns items that actually need repair
        // (m_durability < GetMaxDurability()). The repair-station UI (InventoryGui.RepairOneItem)
        // walks that exact list and stops at the first entry CanRepair() accepts - and CanRepair()
        // never checks durability itself, it trusts GetWornItems() to have already filtered for that.
        // AddEquippedItems() (used for the real equip-state scan) adds every valid sheet item
        // unconditionally, which is correct there but wrong here: an already-full-durability sheet
        // item would sort ahead of a genuinely worn one, RepairOneItem would "repair" it as a no-op
        // and return, and the actually damaged piece would never be reached on that click.
        internal static void AddWornEquippedItems(List<ItemDrop.ItemData> items)
        {
            if (_equipmentInventory == null || items == null)
            {
                return;
            }

            List<ItemDrop.ItemData> equipmentItems = _equipmentInventory.GetAllItems();
            for (int i = 0; i < equipmentItems.Count; i++)
            {
                ItemDrop.ItemData item = equipmentItems[i];
                int slot = item == null ? -1 : GetSlotIndex(item.m_gridPos);
                if (slot >= 0 && IsEquipmentSlot(slot) && MatchesSlot(item, slot) && !items.Contains(item)
                    && item.m_shared.m_useDurability && item.m_durability < item.GetMaxDurability())
                {
                    items.Add(item);
                }
            }
        }

        private static Dictionary<string, string> GetCustomData(Player player)
        {
            return _customDataField == null || player == null
                ? null
                : _customDataField.GetValue(player) as Dictionary<string, string>;
        }

        private static string GetItemDisplayName(ItemDrop.ItemData item)
        {
            return item != null && item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name)
                ? item.m_shared.m_name
                : "<unknown>";
        }

        private static void ShowWrongItemTypeMessage()
        {
            if (Time.time - _lastWrongTypeMessageTime < 2f)
            {
                return;
            }

            _lastWrongTypeMessageTime = Time.time;

            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                    "That item doesn't belong in that slot.", 0, null, false);
            }
        }

    }

    // See BeginContainerUpdateGuard/EndContainerUpdateGuard: UpdateContainer() incorrectly cancels
    // any drag whose source inventory isn't the player's own bag when no container window is open,
    // which fires every frame for a drag started from the equipment sheet. This brackets that one
    // check so the cancellation never triggers for our own inventory.
    [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
    internal static class InventoryGuiUpdateContainerEquipmentSheetPatch
    {
        private static void Prefix(InventoryGui __instance, ref bool __state)
        {
            __state = EquipmentSheetPlugin.BeginContainerUpdateGuard(__instance);
        }

        private static void Postfix(InventoryGui __instance, bool __state)
        {
            EquipmentSheetPlugin.EndContainerUpdateGuard(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class InventoryGuiAwakeEquipmentSheetPatch
    {
        private static void Postfix(InventoryGui __instance)
        {
            EquipmentSheetPlugin.SetupPanel(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Save")]
    internal static class PlayerSaveEquipmentSheetPatch
    {
        private static void Prefix(Player __instance)
        {
            EquipmentSheetPlugin.SaveEquipmentInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "Load")]
    internal static class PlayerLoadEquipmentSheetPatch
    {
        private static void Postfix(Player __instance)
        {
            EquipmentSheetPlugin.LoadEquipmentInventory(__instance);
        }
    }

    [HarmonyPatch(typeof(Player), "CreateTombStone")]
    internal static class PlayerCreateTombStoneEquipmentSheetPatch
    {
        private static void Prefix(Player __instance)
        {
            EquipmentSheetPlugin.MoveSheetItemsToPlayerInventoryForDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "EquipItem", new Type[] { typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class HumanoidEquipItemEquipmentSheetPatch
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            EquipmentSheetPlugin.QueueAutoRouteEquippedItem(__instance, item, __result);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList", new Type[] { typeof(List<Recipe>) })]
    internal static class InventoryGuiUpdateRecipeListEquipmentSheetPatch
    {
        private static void Prefix(InventoryGui __instance, ref bool __state)
        {
            __state = EquipmentSheetPlugin.BeginUpgradeRecipeScan(__instance);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            EquipmentSheetPlugin.EndUpgradeRecipeScan(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting", new Type[] { typeof(Player) })]
    internal static class InventoryGuiDoCraftingEquipmentSheetPatch
    {
        private static void Prefix(InventoryGui __instance, Player player)
        {
            EquipmentSheetPlugin.BeginSheetUpgrade(__instance, player);
        }

        private static Exception Finalizer(Exception __exception)
        {
            EquipmentSheetPlugin.EndSheetUpgrade(__exception);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Inventory), "GetAllItems", new Type[] { typeof(string), typeof(List<ItemDrop.ItemData>) })]
    internal static class InventoryGetAllItemsByNameEquipmentSheetPatch
    {
        private static void Postfix(Inventory __instance, string name, List<ItemDrop.ItemData> items)
        {
            EquipmentSheetPlugin.AddSheetUpgradeItems(__instance, name, items);
        }
    }

    [HarmonyPatch(typeof(Inventory), "ContainsItem", new Type[] { typeof(ItemDrop.ItemData) })]
    internal static class InventoryContainsItemEquipmentSheetPatch
    {
        private static void Postfix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            EquipmentSheetPlugin.IncludeSheetUpgradeItem(__instance, item, ref __result);
        }
    }

    [HarmonyPatch(typeof(Inventory), "RemoveItem", new Type[] { typeof(ItemDrop.ItemData) })]
    internal static class InventoryRemoveItemEquipmentSheetPatch
    {
        private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
        {
            return EquipmentSheetPlugin.RemoveSheetUpgradeItem(__instance, item, ref __result);
        }
    }

    [HarmonyPatch(typeof(Inventory), "AddItem", new Type[]
    {
        typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(bool), typeof(bool)
    })]
    internal static class InventoryAddCraftedItemEquipmentSheetPatch
    {
        private static bool Prefix(Inventory __instance, string name, int stack, int quality, int variant,
            long crafterID, string crafterName, bool cheated, bool pickedUp, ref ItemDrop.ItemData __result)
        {
            return EquipmentSheetPlugin.AddUpgradedSheetItem(__instance, name, stack, quality, variant,
                crafterID, crafterName, cheated, pickedUp, ref __result);
        }
    }

    [HarmonyPatch(typeof(Inventory), "GetTotalWeight")]
    internal static class InventoryTotalWeightEquipmentSheetPatch
    {
        private static void Postfix(Inventory __instance, ref float __result)
        {
            Player player = Player.m_localPlayer;
            if (player != null && __instance == player.GetInventory())
            {
                __result += EquipmentSheetPlugin.GetEquipmentWeight();
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), "GetEquippedItems")]
    internal static class InventoryGetEquippedItemsEquipmentSheetPatch
    {
        private static void Postfix(Inventory __instance, ref List<ItemDrop.ItemData> __result)
        {
            Player player = Player.m_localPlayer;
            if (player != null && __instance == player.GetInventory())
            {
                EquipmentSheetPlugin.AddEquippedItems(__result);
            }
        }
    }

    [HarmonyPatch(typeof(Inventory), "GetWornItems")]
    internal static class InventoryGetWornItemsEquipmentSheetPatch
    {
        private static void Postfix(Inventory __instance, List<ItemDrop.ItemData> worn)
        {
            Player player = Player.m_localPlayer;
            if (player != null && __instance == player.GetInventory())
            {
                EquipmentSheetPlugin.AddWornEquippedItems(worn);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), "DropItem")]
    internal static class InventoryGridDropItemEquipmentSheetPatch
    {
        private static bool Prefix(InventoryGrid __instance, Inventory __0, ItemDrop.ItemData __1, int __2, Vector2i __3, ref bool __result)
        {
            if (!EquipmentSheetPlugin.IsEquipmentGrid(__instance))
            {
                return true;
            }

            __result = EquipmentSheetPlugin.TryDropIntoEquipmentSlot(__0, __1, __2, __3);
            return false;
        }
    }
}
