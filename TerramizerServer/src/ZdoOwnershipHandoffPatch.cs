using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TerramizerServer
{
    [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
    internal static class ZdoOwnershipHandoffPatch
    {
        private static readonly List<ZDO> NearbyObjects =
            new List<ZDO>(512);

        private delegate bool IsInPeerActiveAreaDelegate(
            ZDOMan instance,
            Vector3 position,
            long uid);

        private static readonly IsInPeerActiveAreaDelegate
            IsInPeerActiveArea =
                CreateIsInPeerActiveAreaDelegate();

        private static IsInPeerActiveAreaDelegate
            CreateIsInPeerActiveAreaDelegate()
        {
            try
            {
                var method =
                    AccessTools.Method(
                        typeof(ZDOMan),
                        "IsInPeerActiveArea");

                if (method == null)
                {
                    return null;
                }

                return
                    (IsInPeerActiveAreaDelegate)
                    method.CreateDelegate(
                        typeof(IsInPeerActiveAreaDelegate));
            }
            catch
            {
                return null;
            }
        }

        private static bool OwnerIsInActiveArea(
            ZDOMan instance,
            Vector3 position,
            long owner)
        {
            // If the private Valheim helper could not be bound,
            // fail conservatively: treat the existing owner as
            // active so TerramizerServer never steals ownership.
            if (IsInPeerActiveArea == null)
            {
                return true;
            }

            return
                IsInPeerActiveArea(
                    instance,
                    position,
                    owner);
        }

        private static bool Prefix(
            ZDOMan __instance,
            Vector3 refPosition,
            long uid)
        {
            try
            {
                if (__instance == null ||
                    ZNet.instance == null)
                {
                    return true;
                }

                // Valheim 1.0 native zone representation.
                Vector2s zone =
                    ZoneSystem.GetZone(
                        refPosition);

                // Valheim 1.0 uses synchronized SimulationDistance
                // rather than the old integer active-area fields.
                SimulationDistance synced =
                    ZNet.instance
                        .GetSyncedSimulationDistance();

                SimulationDistance nearOnly =
                    new SimulationDistance(
                        synced.NearSimulationDistance,
                        0,
                        synced.IsClassic);

                NearbyObjects.Clear();

                __instance.FindSectorObjects(
                    zone,
                    nearOnly,
                    NearbyObjects);

                bool isServerPass =
                    uid == ZDOMan.GetSessionID();

                for (int i = 0;
                     i < NearbyObjects.Count;
                     i++)
                {
                    ZDO zdo =
                        NearbyObjects[i];

                    if (zdo == null ||
                        !zdo.Persistent)
                    {
                        continue;
                    }

                    Vector3 position =
                        zdo.GetPosition();

                    bool hasOwner =
                        zdo.HasOwner();

                    long owner;
                    bool ownedByPassPeer;

                    if (isServerPass)
                    {
                        ownedByPassPeer =
                            zdo.IsOwner();

                        owner =
                            ownedByPassPeer ||
                            !hasOwner
                                ? 0L
                                : zdo.GetOwner();
                    }
                    else
                    {
                        owner =
                            hasOwner
                                ? zdo.GetOwner()
                                : 0L;

                        ownedByPassPeer =
                            owner == uid;
                    }

                    if (ownedByPassPeer)
                    {
                        if (!ZNetScene.InActiveArea(
                            position,
                            zone))
                        {
                            zdo.SetOwner(0L);
                        }

                        continue;
                    }

                    bool existingOwnerActive =
                        hasOwner &&
                        OwnerIsInActiveArea(
                            __instance,
                            position,
                            owner);

                    if (!existingOwnerActive &&
                        ZNetScene.InActiveArea(
                            position,
                            zone))
                    {
                        zdo.SetOwner(uid);
                    }
                }

                NearbyObjects.Clear();

                return false;
            }
            catch
            {
                NearbyObjects.Clear();

                // Optimization is fail-open.
                // Vanilla ownership processing runs if anything
                // unexpected changes in a future Valheim build.
                return true;
            }
        }
    }
}