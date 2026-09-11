using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Terramizer
{
    // Valheim's generic SetValue uses object.Equals for value-type values,
    // creating avoidable boxed allocations on hot ZDO/update paths. This
    // replacement keeps the original sorted-array behavior and compares
    // values through EqualityComparer<T>.Default without boxing.
    internal static class BinarySearchDictionarySetValuePatch
    {
        private static readonly MethodInfo PrefixMethod = AccessTools.Method(typeof(BinarySearchDictionarySetValuePatch), nameof(SetValuePrefix));

        internal static void Install(Harmony harmony, Action<string> log)
        {
            try
            {
                PatchSetValue<float>(harmony);
                PatchSetValue<UnityEngine.Vector3>(harmony);
                PatchSetValue<UnityEngine.Quaternion>(harmony);
                PatchSetValue<int>(harmony);
                PatchSetValue<long>(harmony);
                PatchSetValue<string>(harmony);
                PatchSetValue<byte[]>(harmony);
                if (log != null) log("Installed allocation-free BinarySearchDictionary.SetValue patches.");
            }
            catch (Exception ex)
            {
                if (log != null) log("Could not install BinarySearchDictionary.SetValue patches; vanilla behavior remains active: " + ex.Message);
            }
        }

        private static void PatchSetValue<TValue>(Harmony harmony)
        {
            Type dictionaryType = typeof(BinarySearchDictionary<int, TValue>);
            MethodInfo original = AccessTools.Method(dictionaryType, "SetValue", new[] { typeof(int), typeof(TValue) });
            if (original == null) throw new MissingMethodException(dictionaryType.FullName, "SetValue");
            MethodInfo prefix = PrefixMethod.MakeGenericMethod(typeof(TValue));
            harmony.Patch(original, prefix: new HarmonyMethod(prefix));
        }

        private static bool SetValuePrefix<TValue>(BinarySearchDictionary<int, TValue> __instance, int key, TValue value, ref bool __result)
        {
            if (__instance == null) return true;

            int[] keys = Fields<TValue>.Keys(__instance);
            TValue[] values = Fields<TValue>.Values(__instance);
            ushort length = Fields<TValue>.Length(__instance);
            bool exactMatch;
            int index = FindKeyIndex(keys, length, key, out exactMatch);

            if (exactMatch)
            {
                if (EqualityComparer<TValue>.Default.Equals(values[index], value))
                {
                    __result = false;
                    return false;
                }

                values[index] = value;
                __result = true;
                return false;
            }

            if (keys == null || keys.Length <= length)
            {
                int capacity = keys == null || keys.Length == 0 ? 1 : keys.Length + 2;
                int[] expandedKeys = new int[capacity];
                TValue[] expandedValues = new TValue[capacity];
                if (length > 0)
                {
                    Array.Copy(keys, expandedKeys, length);
                    Array.Copy(values, expandedValues, length);
                }
                keys = expandedKeys;
                values = expandedValues;
                Fields<TValue>.Keys(__instance) = keys;
                Fields<TValue>.Values(__instance) = values;
            }

            if (length - index > 0)
            {
                Array.Copy(keys, index, keys, index + 1, length - index);
                Array.Copy(values, index, values, index + 1, length - index);
            }
            keys[index] = key;
            values[index] = value;
            Fields<TValue>.Length(__instance) = (ushort)(length + 1);
            __result = true;
            return false;
        }

        private static int FindKeyIndex(int[] keys, ushort length, int key, out bool exactMatch)
        {
            if (length == 0)
            {
                exactMatch = false;
                return 0;
            }

            int low = 0;
            int high = length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (key == keys[middle])
                {
                    exactMatch = true;
                    return middle;
                }
                if (key < keys[middle]) high = middle - 1;
                else low = middle + 1;
            }

            int comparison = key.CompareTo(keys[low]);
            exactMatch = comparison == 0;
            return comparison > 0 ? low + 1 : low;
        }

        private static class Fields<TValue>
        {
            internal static readonly AccessTools.FieldRef<BinarySearchDictionary<int, TValue>, int[]> Keys =
                AccessTools.FieldRefAccess<BinarySearchDictionary<int, TValue>, int[]>("m_keys");
            internal static readonly AccessTools.FieldRef<BinarySearchDictionary<int, TValue>, TValue[]> Values =
                AccessTools.FieldRefAccess<BinarySearchDictionary<int, TValue>, TValue[]>("m_values");
            internal static readonly AccessTools.FieldRef<BinarySearchDictionary<int, TValue>, ushort> Length =
                AccessTools.FieldRefAccess<BinarySearchDictionary<int, TValue>, ushort>("m_length");
        }
    }
}
