using HarmonyLib;
using UnityEngine;

namespace TerramizerServer
{
    [HarmonyPatch(typeof(AudioMan), "Update")]
    internal static class AudioManHeadlessPatch
    {
        private static bool Prefix() { return !TerramizerServerPlugin.ShouldSkipHeadlessVisualSystems(); }
    }

    [HarmonyPatch(typeof(ShieldDomeImageEffect), "Awake")]
    internal static class ShieldDomeAwakeHeadlessPatch
    {
        private static bool Prefix(ShieldDomeImageEffect __instance)
        {
            if (!TerramizerServerPlugin.ShouldSkipHeadlessVisualSystems()) return true;
            if (__instance != null) ((Behaviour)__instance).enabled = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ShieldDomeImageEffect), "GetDomeColor")]
    internal static class ShieldDomeColorHeadlessPatch
    {
        private static bool Prefix(ref Color __result)
        {
            if (!TerramizerServerPlugin.ShouldSkipHeadlessVisualSystems()) return true;
            __result = Color.white;
            return false;
        }
    }
}
