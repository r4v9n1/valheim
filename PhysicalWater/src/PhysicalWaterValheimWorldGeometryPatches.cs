using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PhysicalWater
{
    internal static class ValheimGeometryDirtyBridge
    {
        internal static void Mark(string label, Component source)
        {
            if (PhysicalWaterPlugin.Settings == null ||
                !PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value ||
                PhysicalWaterValheimWorldGeometryAdapter.Instance == null)
            {
                return;
            }

            PhysicalWaterValheimWorldGeometryAdapter.Instance.MarkDirtyFromValheimEvent(label, source);
        }
    }

    internal static class ValheimGeometryTerrainOperationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TerrainComp), "ApplyOperation");
        }

        private static void Postfix(TerrainComp __instance)
        {
            ValheimGeometryDirtyBridge.Mark("terrain operation", __instance);
        }
    }

    internal static class ValheimGeometryTerrainInternalOperationPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TerrainComp), "InternalDoOperation");
        }

        private static void Postfix(TerrainComp __instance)
        {
            ValheimGeometryDirtyBridge.Mark("terrain internal operation", __instance);
        }
    }

    [HarmonyPatch(typeof(Heightmap), "Regenerate")]
    internal static class ValheimGeometryHeightmapRegeneratePatch
    {
        private static void Postfix(Heightmap __instance)
        {
            ValheimGeometryDirtyBridge.Mark(
                ValheimGeometryHeightmapEventState.ConsumePoke(__instance)
                    ? "heightmap poke/regenerate"
                    : "heightmap regenerate",
                __instance);
        }
    }

    [HarmonyPatch(typeof(Heightmap), "Poke")]
    internal static class ValheimGeometryHeightmapPokePatch
    {
        private static void Prefix(Heightmap __instance)
        {
            ValheimGeometryHeightmapEventState.RegisterPoke(__instance);
        }
    }

    internal static class ValheimGeometryHeightmapEventState
    {
        private static readonly HashSet<int> PendingPokes = new HashSet<int>();

        internal static void RegisterPoke(Heightmap heightmap)
        {
            if (heightmap != null) PendingPokes.Add(heightmap.GetInstanceID());
        }

        internal static bool ConsumePoke(Heightmap heightmap)
        {
            return heightmap != null && PendingPokes.Remove(heightmap.GetInstanceID());
        }
    }

    [HarmonyPatch(typeof(Piece), "OnPlaced")]
    internal static class ValheimGeometryPiecePlacedPatch
    {
        private static void Postfix(Piece __instance)
        {
            ValheimGeometryDirtyBridge.Mark("piece placed", __instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Destroy", new[] { typeof(GameObject) })]
    internal static class ValheimGeometryNetworkDestroyedPatch
    {
        private static void Prefix(GameObject go)
        {
            if (go == null) return;
            Component source = go.GetComponent<Piece>();
            if (source == null) source = go.GetComponent<WearNTear>();
            if (source == null) source = go.GetComponent<Destructible>();
            if (source == null) source = go.GetComponent<MineRock>();
            if (source == null) source = go.GetComponent<MineRock5>();
            if (source != null) ValheimGeometryDirtyBridge.Mark("network geometry destroyed", source);
        }
    }

    [HarmonyPatch(typeof(Door), "SetState")]
    internal static class ValheimGeometryDoorStatePatch
    {
        private static void Prefix(Door __instance, int state, out bool __state)
        {
            Animator animator = __instance != null ? __instance.GetComponentInChildren<Animator>() : null;
            __state = animator != null && animator.GetInteger("state") != state;
        }

        private static void Postfix(Door __instance, bool __state)
        {
            if (__state) ValheimGeometryDirtyBridge.Mark("door/gate state", __instance);
        }
    }

    [HarmonyPatch(typeof(Destructible), "DestroyNow")]
    internal static class ValheimGeometryDestructibleDestroyedPatch
    {
        private static void Postfix(Destructible __instance)
        {
            ValheimGeometryDirtyBridge.Mark("destructible destroyed", __instance);
        }
    }

    internal static class ValheimGeometryMineRockHiddenPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MineRock), "RPC_Hide");
        }

        private static void Postfix(MineRock __instance)
        {
            ValheimGeometryDirtyBridge.Mark("mine rock hidden", __instance);
        }
    }

    internal static class ValheimGeometryMineRock5MeshPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(MineRock5), "UpdateMesh");
        }

        private static void Postfix(MineRock5 __instance)
        {
            ValheimGeometryDirtyBridge.Mark("mine rock mesh update", __instance);
        }
    }
}
