using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BrennivinProtection
{
    internal static class BrennivinEquipmentSheetCompat
    {
        private static bool _lookupComplete;
        private static MethodInfo _moveSheetItemsForDeath;

        internal static bool TryMoveSheetItemsToPlayerInventory(Player player)
        {
            if (player == null)
            {
                return false;
            }

            EnsureLookup();
            if (_moveSheetItemsForDeath == null)
            {
                return false;
            }

            try
            {
                _moveSheetItemsForDeath.Invoke(null, new object[] { player });
                BrennivinPlugin.LogInfo("Asked Equipment Sheet to move sheet items into the death inventory before Brennivin protection check.");
                return true;
            }
            catch (Exception ex)
            {
                BrennivinPlugin.LogWarning("Equipment Sheet compatibility call failed: " + ex.Message);
                return false;
            }
        }

        private static void EnsureLookup()
        {
            if (_lookupComplete)
            {
                return;
            }

            _lookupComplete = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType("EquipmentSheet.EquipmentSheetPlugin", false);
                if (type == null)
                {
                    continue;
                }

                _moveSheetItemsForDeath = type.GetMethod(
                    "MoveSheetItemsToPlayerInventoryForDeath",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return;
            }
        }
    }

    internal static class BrennivinBedTeleport
    {
        internal static bool TryTeleportToClaimedBed(Player player)
        {
            if (player == null || Game.instance == null)
            {
                return false;
            }

            PlayerProfile profile = Game.instance.GetPlayerProfile();
            if (profile == null || !profile.HaveCustomSpawnPoint())
            {
                BrennivinPlugin.Message(player, "Brennivin needs a claimed bed.");
                return false;
            }

            Vector3 spawnPoint = profile.GetCustomSpawnPoint();
            if (spawnPoint == Vector3.zero)
            {
                BrennivinPlugin.Message(player, "Brennivin could not find your claimed bed.");
                return false;
            }

            if (player.IsTeleporting())
            {
                BrennivinPlugin.Message(player, "Brennivin cannot be used while teleporting.");
                return false;
            }

            bool started = player.TeleportTo(spawnPoint, player.transform.rotation, true);
            if (!started)
            {
                BrennivinPlugin.Message(player, "Brennivin could not start the teleport.");
                return false;
            }

            BrennivinWakeupAnimation.Queue(player);
            BrennivinPlugin.Message(player, "Brennivin carries you home.");
            BrennivinPlugin.LogInfo("Started Brennivin bed teleport to " + spawnPoint + ".");
            return true;
        }
    }

    internal sealed class BrennivinWakeupAnimation : MonoBehaviour
    {
        private static readonly FieldInfo AnimatorField = AccessTools.Field(typeof(Character), "m_animator");
        private static readonly FieldInfo WakeupTimerField = AccessTools.Field(typeof(Player), "m_wakeupTimer");

        private Player _player;
        private float _timeout;
        private float _settleDelay = 0.25f;

        internal static void Queue(Player player)
        {
            if (player == null)
            {
                return;
            }

            BrennivinWakeupAnimation existing = player.GetComponent<BrennivinWakeupAnimation>();
            if (existing == null)
            {
                existing = player.gameObject.AddComponent<BrennivinWakeupAnimation>();
            }

            existing._player = player;
            existing._timeout = 18f;
            existing._settleDelay = 0.25f;
        }

        private void Update()
        {
            if (_player == null)
            {
                Destroy(this);
                return;
            }

            _timeout -= Time.deltaTime;
            if (_timeout <= 0f)
            {
                Destroy(this);
                return;
            }

            if (_player.IsTeleporting())
            {
                return;
            }

            _settleDelay -= Time.deltaTime;
            if (_settleDelay > 0f)
            {
                return;
            }

            PulseWakeup(_player);
            Destroy(this);
        }

        private static void PulseWakeup(Player player)
        {
            try
            {
                ZNetView view = player.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.IsOwner() && view.GetZDO() != null)
                {
                    view.GetZDO().Set(ZDOVars.s_wakeup, true);
                }

                Animator animator = AnimatorField == null ? null : AnimatorField.GetValue(player) as Animator;
                if (animator != null)
                {
                    animator.SetBool("wakeup", true);
                }

                if (WakeupTimerField != null)
                {
                    WakeupTimerField.SetValue(player, 0f);
                }

                BrennivinPlugin.LogInfo("Pulsed Valheim wakeup animation after Brennivin teleport.");
            }
            catch (Exception ex)
            {
                BrennivinPlugin.LogWarning("Brennivin wakeup animation pulse failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "CreateTombStone")]
    [HarmonyPriority(Priority.First)]
    [HarmonyBefore(new[] { "r4v9n1.equipmentsheet" })]
    internal static class PlayerCreateTombStoneBrennivinPatch
    {
        private static bool Prefix(Player __instance)
        {
            if (!BrennivinPlugin.IsDeathProtectionEnabled() || __instance == null)
            {
                return true;
            }

            Inventory inventory = __instance.GetInventory();
            if (BrennivinPlugin.TryConsumeOne(inventory))
            {
                BrennivinPlugin.Message(__instance, "Brennivin was spent. Your inventory stayed with you.");
                BrennivinPlugin.LogInfo("Brennivin death protection consumed one carried flask and skipped tombstone creation.");
                return false;
            }

            if (BrennivinEquipmentSheetCompat.TryMoveSheetItemsToPlayerInventory(__instance) &&
                BrennivinPlugin.TryConsumeOne(inventory))
            {
                BrennivinPlugin.Message(__instance, "Brennivin was spent. Your inventory stayed with you.");
                BrennivinPlugin.LogInfo("Brennivin death protection consumed one flask after Equipment Sheet items were moved into the player inventory.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(Player), "ConsumeItem", new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(bool) })]
    internal static class PlayerConsumeItemBrennivinPatch
    {
        private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, bool checkWorldLevel, ref bool __result)
        {
            if (!BrennivinPlugin.IsBrennivin(item))
            {
                return true;
            }

            __result = false;
            if (__instance == null || inventory == null || !inventory.ContainsItem(item))
            {
                return false;
            }

            if (!BrennivinPlugin.IsBedTeleportEnabled())
            {
                BrennivinPlugin.Message(__instance, "Brennivin bed teleport is disabled.");
                return false;
            }

            if (!__instance.CanConsumeItem(item, checkWorldLevel))
            {
                return false;
            }

            if (!BrennivinBedTeleport.TryTeleportToClaimedBed(__instance))
            {
                return false;
            }

            __result = inventory.RemoveOneItem(item);
            return false;
        }
    }
}
