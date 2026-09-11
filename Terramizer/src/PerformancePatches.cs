using HarmonyLib;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Terramizer
{
    internal static class PlacementEffectLimiter
    {
        private static int _scopeDepth;
        private static readonly List<EffectList> _activeEffects = new List<EffectList>(4);

        internal static bool Begin(Component component, EffectList effect)
        {
            Player local = Player.m_localPlayer;
            if (component == null || effect == null || local == null ||
                (local.transform.position - component.transform.position).sqrMagnitude > 625f)
                return false;
            _scopeDepth++;
            _activeEffects.Add(effect);
            return true;
        }

        internal static void End(bool entered)
        {
            if (!entered || _scopeDepth <= 0) return;
            _scopeDepth--;
            if (_activeEffects.Count > 0) _activeEffects.RemoveAt(_activeEffects.Count - 1);
        }

        internal static void RemovePlacementVisuals(EffectList effect, GameObject[] created)
        {
            if (_scopeDepth <= 0 || effect == null || !_activeEffects.Contains(effect) || created == null)
                return;

            for (int i = 0; i < created.Length; i++)
            {
                GameObject instance = created[i];
                if (instance == null)
                    continue;

                // Building placement uses particle-only vfx_Place_* prefabs for the
                // sudden dust/smoke burst. They do not carry Valheim's Smoke
                // component, so component/name scanning alone cannot remove them.
                // Keep the scope limited to placement VFX; fire and environmental
                // smoke are never routed through these prefabs.
                if (instance.name.StartsWith("vfx_Place_", StringComparison.OrdinalIgnoreCase))
                {
                    SuppressSmokeObject(instance);
                    continue;
                }

                Smoke[] smokeComponents = instance.GetComponentsInChildren<Smoke>(true);
                bool removedSmoke = false;
                for (int j = 0; j < smokeComponents.Length; j++)
                {
                    if (smokeComponents[j] != null)
                    {
                        SuppressSmokeObject(smokeComponents[j].gameObject);
                        removedSmoke = true;
                    }
                }

                if (!removedSmoke)
                {
                    Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < transforms.Length; j++)
                    {
                        Transform candidate = transforms[j];
                        if (candidate != null && candidate.name.IndexOf("smoke", StringComparison.OrdinalIgnoreCase) >= 0)
                            SuppressSmokeObject(candidate.gameObject);
                    }
                }
            }
        }

        private static void SuppressSmokeObject(GameObject smokeObject)
        {
            if (smokeObject == null) return;
            ParticleSystem[] particles = smokeObject.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                    particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            smokeObject.SetActive(false);
            UnityEngine.Object.Destroy(smokeObject);
        }

    }

    [HarmonyPatch(typeof(Piece), "OnPlaced")]
    internal static class PiecePlacementEffectScopePatch
    {
        private static void Prefix(Piece __instance, ref bool __state) { __state = PlacementEffectLimiter.Begin(__instance, __instance != null ? __instance.m_placeEffect : null); }
        private static System.Exception Finalizer(System.Exception __exception, bool __state) { PlacementEffectLimiter.End(__state); return __exception; }
    }

    [HarmonyPatch(typeof(Player), "PlacePiece", new[] { typeof(Piece), typeof(Vector3), typeof(Quaternion), typeof(bool), typeof(bool) })]
    internal static class PlayerPlacePieceEffectScopePatch
    {
        private static void Prefix(Player __instance, Piece piece, ref bool __state)
        {
            __state = PlacementEffectLimiter.Begin(__instance, piece != null ? piece.m_placeEffect : null);
        }

        private static System.Exception Finalizer(System.Exception __exception, bool __state)
        {
            PlacementEffectLimiter.End(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(EffectList), "Create")]
    internal static class PlacementEffectCleanupPatch
    {
        private static void Postfix(EffectList __instance, GameObject[] __result) { PlacementEffectLimiter.RemovePlacementVisuals(__instance, __result); }
    }
}
