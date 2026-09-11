using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace TerramizerServer
{
    public sealed partial class TerramizerServerPlugin
    {
        private static ConfigEntry<bool> _enableExtendedTerrainLimits;
        private static ConfigEntry<float> _terrainRaiseLimitMeters;
        private static ConfigEntry<float> _terrainDigLimitMeters;

        private void BindTerrainCompatibilityConfig()
        {
            _enableExtendedTerrainLimits = Config.Bind("Terrain", "EnableExtendedTerrainLimits", true,
                "Enable the dedicated-server terrain limit extension for compatible clients and local server authority.");
            _terrainRaiseLimitMeters = BindRange("Terrain", "TerrainRaiseLimitMeters", 16f,
                "Maximum terrain raise/level delta from original terrain.", 16f, 64f);
            _terrainDigLimitMeters = BindRange("Terrain", "TerrainDigLimitMeters", 16f,
                "Maximum terrain dig/lower delta from original terrain.", 16f, 64f);
        }

        internal static float GetTerrainRaiseLimitMeters()
        {
            return _enableExtendedTerrainLimits != null && _enableExtendedTerrainLimits.Value && _terrainRaiseLimitMeters != null
                ? UnityEngine.Mathf.Max(16f, _terrainRaiseLimitMeters.Value) : 8f;
        }

        internal static float GetTerrainDigLimitMeters()
        {
            return _enableExtendedTerrainLimits != null && _enableExtendedTerrainLimits.Value && _terrainDigLimitMeters != null
                ? UnityEngine.Mathf.Max(16f, _terrainDigLimitMeters.Value) : 8f;
        }

        internal static float GetTerrainDigThresholdMeters()
        {
            return UnityEngine.Mathf.Max(0f, GetTerrainDigLimitMeters() - 0.05f);
        }

    }

    [HarmonyPatch(typeof(Heightmap), "AtMaxWorldLevelDepth")]
    internal static class HeightmapAtMaxWorldLevelDepthTerrainLimitPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value - 7.9499998f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainDigThresholdMeters)));
                }
                else
                    yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one Heightmap.AtMaxWorldLevelDepth terrain-limit constant, found " + replaced + ".");
        }
    }

    [HarmonyPatch(typeof(Heightmap), "LevelTerrain")]
    internal static class HeightmapLevelTerrainTerrainLimitPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R8 && instruction.operand is double value && Math.Abs(value - 8d) < 0.0001d && ++replaced <= 2)
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), replaced == 1 ? nameof(TerramizerServerPlugin.GetTerrainDigLimitMeters) : nameof(TerramizerServerPlugin.GetTerrainRaiseLimitMeters)));
                    yield return new CodeInstruction(OpCodes.Conv_R8);
                }
                else yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Expected two Heightmap.LevelTerrain terrain-limit constants, found " + replaced + ".");
        }
    }

    [HarmonyPatch(typeof(TerrainComp), "LevelTerrain")]
    internal static class TerrainCompLevelTerrainLimitPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value + 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainDigLimitMeters)));
                    yield return new CodeInstruction(OpCodes.Neg);
                }
                else if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float positive && Math.Abs(positive - 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainRaiseLimitMeters)));
                }
                else yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Expected signed TerrainComp.LevelTerrain terrain-limit constants, found " + replaced + ".");
        }
    }

    [HarmonyPatch(typeof(TerrainComp), "RaiseTerrain")]
    internal static class TerrainCompRaiseTerrainLimitPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value + 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainDigLimitMeters)));
                    yield return new CodeInstruction(OpCodes.Neg);
                }
                else if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float positive && Math.Abs(positive - 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainRaiseLimitMeters)));
                }
                else yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Expected signed TerrainComp.RaiseTerrain terrain-limit constants, found " + replaced + ".");
        }
    }

    [HarmonyPatch(typeof(TerrainComp), "ApplyToHeightmap")]
    internal static class TerrainCompApplyToHeightmapTerrainLimitPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value + 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainDigLimitMeters)));
                    yield return new CodeInstruction(OpCodes.Neg);
                }
                else if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float positive && Math.Abs(positive - 8f) < 0.0001f)
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TerramizerServerPlugin), nameof(TerramizerServerPlugin.GetTerrainRaiseLimitMeters)));
                }
                else yield return instruction;
            }
            if (replaced != 2) throw new InvalidOperationException("Expected signed TerrainComp.ApplyToHeightmap terrain-limit constants, found " + replaced + ".");
        }
    }
}
