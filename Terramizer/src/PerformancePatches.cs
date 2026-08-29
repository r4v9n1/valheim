using HarmonyLib;
using UnityEngine;

namespace Terramizer
{
    [HarmonyPatch(typeof(ClutterSystem), "ResetGrass")]
    internal static class ClutterSystemResetGrassCoalescePatch
    {
        private static bool Prefix(Vector3 center, float radius)
        {
            return TerramizerPlugin.QueueClutterReset(center, radius);
        }
    }

    [HarmonyPatch(typeof(ClutterSystem), "UpdateGrass")]
    internal static class ClutterSystemUpdateGrassPacingPatch
    {
        private static bool Prefix(bool rebuildAll)
        {
            return TerramizerPlugin.ShouldRunClutterUpdate(rebuildAll);
        }
    }

    [HarmonyPatch(typeof(Heightmap), "Regenerate")]
    internal static class HeightmapRegenerateObservationPatch
    {
        private static void Postfix()
        {
            TerramizerPlugin.MarkHeightmapRegenerated();
        }
    }

    [HarmonyPatch(typeof(WearNTearUpdater), "UpdateWearNTear")]
    internal static class WearNTearUpdaterPacingPatch
    {
        private static bool Prefix()
        {
            return TerramizerPlugin.ShouldRunWearNTearUpdate();
        }
    }

    [HarmonyPatch(typeof(SmokeRenderer), "LateUpdate")]
    internal static class SmokeRendererPacingPatch
    {
        private static bool Prefix(SmokeRenderer __instance)
        {
            return TerramizerPlugin.ShouldRunSmokeUpdate(__instance);
        }
    }
}
