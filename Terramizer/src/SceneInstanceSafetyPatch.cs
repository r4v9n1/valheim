using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Terramizer
{
    [HarmonyPatch(typeof(ZNetScene), "RemoveObjects")]
    internal static class SceneInstanceSafetyPatch
    {
        private static readonly FieldInfo InstancesField = AccessTools.Field(typeof(ZNetScene), "m_instances");
        private static readonly List<ZDO> StaleKeys = new List<ZDO>(32);
        private static bool _loggedUnavailable;
        private static bool _loggedRepair;

        private static void Prefix(ZNetScene __instance)
        {
            if (__instance == null || InstancesField == null)
            {
                if (!_loggedUnavailable)
                {
                    _loggedUnavailable = true;
                    Debug.LogWarning("Terramizer could not inspect ZNetScene.m_instances; vanilla scene cleanup remains active.");
                }
                return;
            }

            try
            {
                var instances = InstancesField.GetValue(__instance) as Dictionary<ZDO, ZNetView>;
                if (instances == null || instances.Count == 0) return;
                StaleKeys.Clear();
                foreach (KeyValuePair<ZDO, ZNetView> entry in instances)
                {
                    ZDO zdo = entry.Key;
                    ZNetView view = entry.Value;
                    ZDO viewZdo = null;
                    if (view != null)
                    {
                        try { viewZdo = view.GetZDO(); } catch { }
                    }
                    if (zdo == null || !zdo.IsValid() || view == null || viewZdo == null || viewZdo != zdo)
                        StaleKeys.Add(zdo);
                }

                int removed = 0;
                for (int i = 0; i < StaleKeys.Count; i++)
                {
                    ZDO zdo = StaleKeys[i];
                    ZNetView view;
                    if (!instances.TryGetValue(zdo, out view) || !instances.Remove(zdo)) continue;
                    removed++;
                    if (view != null)
                    {
                        try
                        {
                            if (view.GetZDO() == null) UnityEngine.Object.Destroy(view.gameObject);
                        }
                        catch { }
                    }
                }
                StaleKeys.Clear();
                if (removed > 0 && !_loggedRepair)
                {
                    _loggedRepair = true;
                    Debug.LogWarning("Terramizer removed stale ZNetScene instance registrations before vanilla cleanup (count=" + removed + ").");
                }
            }
            catch (Exception ex)
            {
                StaleKeys.Clear();
                if (!_loggedUnavailable)
                {
                    _loggedUnavailable = true;
                    Debug.LogWarning("Terramizer scene-instance sanitation failed; vanilla cleanup remains active: " + ex.Message);
                }
            }
        }
    }
}
