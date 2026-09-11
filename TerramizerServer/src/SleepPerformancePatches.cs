using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace TerramizerServer
{
    [HarmonyPatch(typeof(EnvMan), "SkipToMorning")]
    internal static class EnvManSkipToMorningPatch
    {
        private static void Postfix(EnvMan __instance)
        {
            TerramizerServerPlugin.TuneSleepFastForward(__instance);
        }
    }

    [HarmonyPatch(typeof(Game), "SleepStop")]
    internal static class GameSleepStopPatch
    {
        private static void Prefix(Game __instance, out float __state)
        {
            __state = TerramizerServerPlugin.CaptureSleepSaveTimer(__instance);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo worldSave = AccessTools.Method(
                typeof(ZNet),
                "Save",
                new[] { typeof(bool), typeof(bool), typeof(bool) });

            MethodInfo worldReplacement = AccessTools.Method(
                typeof(TerramizerServerPlugin),
                nameof(TerramizerServerPlugin.SaveWorldFromSleep));

            if (worldSave == null)
                throw new System.InvalidOperationException(
                    "Could not resolve ZNet.Save(bool, bool, bool).");

            if (worldReplacement == null)
                throw new System.InvalidOperationException(
                    "Could not resolve TerramizerServer SaveWorldFromSleep replacement.");

            int worldReplacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(worldSave))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = worldReplacement;
                    worldReplacements++;
                }

                yield return instruction;
            }

            if (worldReplacements != 1)
            {
                throw new System.InvalidOperationException(
                    "Expected one world-save call in Game.SleepStop, found " +
                    worldReplacements + ".");
            }
        }

        private static void Postfix(Game __instance, float __state)
        {
            TerramizerServerPlugin.RestoreSleepSaveTimer(__instance, __state);
        }
    }

    public sealed partial class TerramizerServerPlugin
    {
        internal static void TuneSleepFastForward(EnvMan envMan)
        {
            if (!Application.isBatchMode ||
                _speedUpSleepFastForward == null ||
                !_speedUpSleepFastForward.Value ||
                envMan == null ||
                !envMan.IsTimeSkipping() ||
                ZNet.instance == null ||
                !ZNet.instance.IsServer() ||
                _envManSkipToTimeField == null ||
                _envManTimeSkipSpeedField == null)
            {
                return;
            }

            double remaining =
                (double)_envManSkipToTimeField.GetValue(envMan) -
                ZNet.instance.GetTimeSeconds();

            if (remaining > 0d)
            {
                _envManTimeSkipSpeedField.SetValue(
                    envMan,
                    remaining /
                    Mathf.Clamp(_sleepFastForwardSeconds.Value, 1f, 60f));
            }
        }

        internal static float CaptureSleepSaveTimer(Game game)
        {
            if (game == null ||
                _skipSleepWorldSave == null ||
                !_skipSleepWorldSave.Value ||
                _preserveAutosaveTimer == null ||
                !_preserveAutosaveTimer.Value ||
                _gameSaveTimerField == null)
            {
                return float.NaN;
            }

            return (float)_gameSaveTimerField.GetValue(game);
        }

        internal static void RestoreSleepSaveTimer(Game game, float timer)
        {
            if (game == null ||
                float.IsNaN(timer) ||
                _gameSaveTimerField == null)
            {
                return;
            }

            _gameSaveTimerField.SetValue(game, timer);
        }

        internal static void SaveWorldFromSleep(
            ZNet znet,
            bool sync,
            bool saveOtherPlayerProfiles,
            bool waitForNextFrame)
        {
            if (_skipSleepWorldSave == null || !_skipSleepWorldSave.Value)
            {
                if (znet != null)
                {
                    znet.Save(
                        sync,
                        saveOtherPlayerProfiles,
                        waitForNextFrame);
                }

                return;
            }

            if (_logSkippedSleepSaves != null &&
                _logSkippedSleepSaves.Value &&
                _log != null)
            {
                _log.LogInfo(
                    "Skipped sleep-triggered world save. Regular autosaves remain enabled.");
            }
        }
    }
}