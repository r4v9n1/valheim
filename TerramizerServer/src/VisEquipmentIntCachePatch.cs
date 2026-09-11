using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace TerramizerServer
{
    // VisEquipment.UpdateEquipmentVisuals reads the same ZDO integer table many
    // times per update. ZDO.GetInt performs the table lookup on every call. The
    // active table is stable for the duration of this method, so route those
    // reads directly through the already-built BinarySearchDictionary.
    [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
    internal static class VisEquipmentIntCachePatch
    {
        private static readonly MethodInfo ZdoGetInt = AccessTools.Method(typeof(ZDO), "GetInt", new[] { typeof(int), typeof(int) });
        private static readonly MethodInfo GetCachedIntMethod = AccessTools.Method(typeof(VisEquipmentIntCachePatch), nameof(GetCachedInt));
        private static readonly AccessTools.FieldRef<Dictionary<ZDOID, BinarySearchDictionary<int, int>>> IntTables =
            AccessTools.StaticFieldRefAccess<Dictionary<ZDOID, BinarySearchDictionary<int, int>>>(AccessTools.Field(typeof(ZDOExtraData), "s_ints"));
        private static bool _transpilerFoundCall;

        private static void Prefix(VisEquipment __instance, out BinarySearchDictionary<int, int> __state)
        {
            __state = _currentTable;
            _currentTable = null;
            if (__instance == null)
                return;

            ZNetView nview = __instance.m_nViewOverride != null ? __instance.m_nViewOverride : __instance.GetComponent<ZNetView>();
            if (nview == null)
                return;

            ZDO zdo = nview.GetZDO();
            if (zdo == null)
                return;

            Dictionary<ZDOID, BinarySearchDictionary<int, int>> tables = IntTables();
            if (tables != null)
                tables.TryGetValue(zdo.m_uid, out _currentTable);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (ZdoGetInt == null || GetCachedIntMethod == null)
                throw new MissingMethodException("VisEquipmentIntCachePatch could not resolve ZDO.GetInt(int, int).");

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(ZdoGetInt))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = GetCachedIntMethod;
                    _transpilerFoundCall = true;
                }
                yield return instruction;
            }

            if (!_transpilerFoundCall)
                throw new InvalidOperationException("VisEquipment.UpdateEquipmentVisuals contained no ZDO.GetInt(int, int) calls.");
        }

        private static Exception Finalizer(Exception __exception, BinarySearchDictionary<int, int> __state)
        {
            _currentTable = __state;
            return __exception;
        }

        private static int GetCachedInt(ZDO zdo, int hash, int defaultValue)
        {
            BinarySearchDictionary<int, int> table = CurrentTable;
            if (table != null)
                return table.GetValueOrDefault(hash, defaultValue);
            return zdo != null ? zdo.GetInt(hash, defaultValue) : defaultValue;
        }

        [ThreadStatic]
        private static BinarySearchDictionary<int, int> _currentTable;

        private static BinarySearchDictionary<int, int> CurrentTable
        {
            get { return _currentTable; }
        }
    }
}
