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
                (!PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value &&
                 !PhysicalWaterPlugin.Settings.StageE1Enabled.Value) ||
                PhysicalWaterValheimWorldGeometryAdapter.Instance == null)
            {
                return;
            }

            PhysicalWaterValheimWorldGeometryAdapter.Instance.MarkDirtyFromValheimEvent(label, source);
        }

        internal static void Mark(string label, Component source, Bounds dirtyWorldBounds)
        {
            if (PhysicalWaterPlugin.Settings == null ||
                (!PhysicalWaterPlugin.Settings.ValheimGeometryDiagnosticsEnabled.Value &&
                 !PhysicalWaterPlugin.Settings.StageE1Enabled.Value) ||
                PhysicalWaterValheimWorldGeometryAdapter.Instance == null)
            {
                return;
            }

            PhysicalWaterValheimWorldGeometryAdapter.Instance.MarkDirtyFromValheimEvent(label, source, dirtyWorldBounds);
        }
    }

    [HarmonyPatch(typeof(TerrainComp), "DoOperation")]
    internal static class ValheimGeometryTerrainOperationPatch
    {
        private static void Prefix(TerrainComp __instance, Vector3 pos, TerrainOp.Settings modifier)
        {
            if (__instance == null || modifier == null) return;
            float radius = 0.5f;
            if (modifier.m_level) radius = Mathf.Max(radius, modifier.m_levelRadius);
            if (modifier.m_raise) radius = Mathf.Max(radius, modifier.m_raiseRadius);
            if (modifier.m_smooth) radius = Mathf.Max(radius, modifier.m_smoothRadius);
            if (modifier.m_paintCleared) radius = Mathf.Max(radius, modifier.m_paintRadius);
            float vertical = 8f + Mathf.Abs(modifier.m_levelOffset) + Mathf.Abs(modifier.m_raiseDelta);
            Heightmap heightmap = Heightmap.FindHeightmap(pos);
            ValheimGeometryHeightmapEventState.RegisterTerrainOperation(
                heightmap,
                new Bounds(pos, new Vector3(2f * radius, 2f * vertical, 2f * radius)));
            PhysicalWaterDevE1Runtime.Instance?.OneHitTerrainTruth?.OnTerrainOperationPrefix(
                heightmap,
                pos,
                modifier,
                new Bounds(pos, new Vector3(2f * radius, 2f * vertical, 2f * radius)));
        }
    }

    [HarmonyPatch(typeof(Heightmap), "Regenerate")]
    internal static class ValheimGeometryHeightmapRegeneratePatch
    {
        private static void Postfix(Heightmap __instance)
        {
            PhysicalWaterDevE1Runtime.Instance?.OneHitTerrainTruth?.OnHeightmapRegenerated(__instance);
            bool poke = ValheimGeometryHeightmapEventState.ConsumePoke(__instance);
            if (ValheimGeometryHeightmapEventState.TryConsumeTerrainOperation(__instance, out Bounds dirtyWorldBounds))
            {
                ValheimGeometryDirtyBridge.Mark(
                    poke ? "heightmap terrain operation/regenerate" : "heightmap terrain operation",
                    __instance,
                    dirtyWorldBounds);
                return;
            }

            ValheimGeometryDirtyBridge.Mark(poke ? "heightmap poke/regenerate" : "heightmap regenerate", __instance);
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
        private static readonly Dictionary<int, Bounds> PendingTerrainOperations = new Dictionary<int, Bounds>();

        internal static void RegisterPoke(Heightmap heightmap)
        {
            if (heightmap != null) PendingPokes.Add(heightmap.GetInstanceID());
        }

        internal static bool ConsumePoke(Heightmap heightmap)
        {
            return heightmap != null && PendingPokes.Remove(heightmap.GetInstanceID());
        }

        internal static void RegisterTerrainOperation(Heightmap heightmap, Bounds dirtyWorldBounds)
        {
            if (heightmap == null) return;
            RegisterTerrainOperation(heightmap.GetInstanceID(), dirtyWorldBounds);
        }

        internal static void RegisterTerrainOperation(int heightmapId, Bounds dirtyWorldBounds)
        {
            if (heightmapId == 0) return;
            if (PendingTerrainOperations.TryGetValue(heightmapId, out Bounds pending))
            {
                pending.Encapsulate(dirtyWorldBounds);
                PendingTerrainOperations[heightmapId] = pending;
            }
            else
            {
                PendingTerrainOperations.Add(heightmapId, dirtyWorldBounds);
            }
        }

        internal static bool TryConsumeTerrainOperation(Heightmap heightmap, out Bounds dirtyWorldBounds)
        {
            if (heightmap == null)
            {
                dirtyWorldBounds = default(Bounds);
                return false;
            }
            return TryConsumeTerrainOperation(heightmap.GetInstanceID(), out dirtyWorldBounds);
        }

        internal static bool TryConsumeTerrainOperation(int heightmapId, out Bounds dirtyWorldBounds)
        {
            if (!PendingTerrainOperations.TryGetValue(heightmapId, out dirtyWorldBounds)) return false;
            PendingTerrainOperations.Remove(heightmapId);
            return true;
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

    [HarmonyPatch(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) })]
    internal static class ValheimGeometryStreamedSourceAppearedPatch
    {
        private static void Postfix(GameObject __result)
        {
            if (__result == null) return;
            ZNetView view = __result.GetComponent<ZNetView>();
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (view != null && adapter != null && adapter.IsStreamedSourceNearActiveDomain(view))
                ValheimGeometryDirtyBridge.Mark("streamed source appeared", view);
        }
    }

    // ResetZDO is the single physical-lifecycle point shared by explicit
    // destruction, ZDO destruction, streaming removal, and scene shutdown.
    // The prefix runs while the ZDO identity and GameObject hierarchy are
    // still intact, so PCE can remove exactly the cached source without
    // enumerating ZNetScene.m_instances or rediscovering world roots.
    [HarmonyPatch(typeof(ZNetView), "ResetZDO")]
    internal static class ValheimGeometryStreamedSourceDisappearedPatch
    {
        private static void Prefix(ZNetView __instance)
        {
            PhysicalWaterValheimWorldGeometryAdapter adapter = PhysicalWaterValheimWorldGeometryAdapter.Instance;
            if (adapter != null && adapter.IsCachedStreamedSource(__instance))
                ValheimGeometryDirtyBridge.Mark("streamed source disappeared", __instance);
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
