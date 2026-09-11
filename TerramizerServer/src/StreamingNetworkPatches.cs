using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TerramizerServer
{
    // Zone-entry prefetch.
    //
    // Only force-queues already existing ZDOs. It never creates zones or
    // scene objects. Valheim 1.0 uses Vector2s zones and a per-peer
    // SimulationDistance instead of ZoneSystem.m_activeArea.
    public sealed partial class TerramizerServerPlugin
    {
        private static bool CanRunServerStreaming()
        {
            return
                _enabled != null &&
                _enabled.Value &&
                ZNet.instance != null &&
                ZNet.instance.IsServer() &&
                (!_dedicatedOnly.Value ||
                 ZNet.instance.IsDedicated());
        }

        private struct PeerState
        {
            internal bool HasZone;
            internal Vector2s Zone;
            internal float NextTime;
        }

        private static ConfigEntry<bool>
            _streamingBoostEnabled;

        private static ConfigEntry<int>
            _streamingBoostMaxZdos;

        private static ConfigEntry<float>
            _streamingBoostCooldown;

        private static ConfigEntry<int>
            _streamingBoostMaxQueuePercent;

        private static readonly Dictionary<long, PeerState>
            _streamingPeerStates =
                new Dictionary<long, PeerState>();

        private static readonly List<ZDO>
            _streamingNear =
                new List<ZDO>(512);

        private static readonly List<ZDO>
            _streamingCandidates =
                new List<ZDO>(512);

        private static readonly HashSet<ZDOID>
            _streamingSeen =
                new HashSet<ZDOID>();

        private static Vector3
            _streamingReferencePosition;

        private static float
            _nextStreamingPassTime;

        private static bool
            _streamingFailureLogged;

        private void InitializeStreaming()
        {
            _streamingBoostEnabled =
                Config.Bind(
                    "Streaming",
                    "EnableServerZoneStreamingBoost",
                    true,
                    "Prefetches a bounded set of existing nearby ZDOs when a ready peer enters a new zone. Does not create zones or scene objects.");

            _streamingBoostMaxZdos =
                BindRange(
                    "Streaming",
                    "MaxZoneStreamingBoostZdosPerPeer",
                    256,
                    "Maximum existing ZDOs force-queued for one peer per zone transition.",
                    0,
                    2000);

            _streamingBoostCooldown =
                BindRange(
                    "Streaming",
                    "ZoneStreamingBoostCooldownSeconds",
                    0.75f,
                    "Minimum time between a peer's bounded streaming boosts.",
                    0.1f,
                    10f);

            _streamingBoostMaxQueuePercent =
                BindRange(
                    "Streaming",
                    "ZoneStreamingBoostMaxQueuePercent",
                    35,
                    "Do not prefetch while the peer socket queue is above this percentage of its ceiling.",
                    10,
                    90);
        }

        private static void RunStreamingBoost()
        {
            if (_streamingBoostEnabled == null ||
                !_streamingBoostEnabled.Value ||
                !CanRunServerStreaming() ||
                ZNet.instance == null ||
                !ZNet.instance.IsDedicated() ||
                ZDOMan.instance == null ||
                ZoneSystem.instance == null)
            {
                return;
            }

            try
            {
                float now =
                    Time.realtimeSinceStartup;

                if (now < _nextStreamingPassTime)
                {
                    return;
                }

                _nextStreamingPassTime =
                    now + 0.1f;

                var peers =
                    ZNet.instance.GetConnectedPeers();

                if (peers == null)
                {
                    return;
                }

                for (int i = 0; i < peers.Count; i++)
                {
                    ZNetPeer peer =
                        peers[i];

                    if (peer == null ||
                        !peer.IsReady() ||
                        peer.m_socket == null)
                    {
                        continue;
                    }

                    Vector3 position =
                        peer.GetRefPos();

                    // Native Valheim 1.0 zone representation.
                    Vector2s zone =
                        ZoneSystem.GetZone(position);

                    PeerState state;

                    if (!_streamingPeerStates.TryGetValue(
                        peer.m_uid,
                        out state))
                    {
                        state =
                            new PeerState();
                    }

                    if (now < state.NextTime ||
                        (state.HasZone &&
                         state.Zone == zone))
                    {
                        continue;
                    }

                    const int vanillaSendQueueCeiling =
                        10240;

                    int queue =
                        peer.m_socket.GetSendQueueSize();

                    if (queue * 100L >=
                        (long)vanillaSendQueueCeiling *
                        _streamingBoostMaxQueuePercent.Value)
                    {
                        state.NextTime =
                            now + 0.25f;

                        _streamingPeerStates[peer.m_uid] =
                            state;

                        continue;
                    }

                    _streamingNear.Clear();
                    _streamingCandidates.Clear();
                    _streamingSeen.Clear();

                    // Valheim 1.0 uses the peer's SimulationDistance.
                    //
                    // This prefetch only wants the near set, so retain
                    // the peer's near distance and explicitly set the
                    // distant ring to zero.
                    SimulationDistance peerDistance =
                        peer.m_simulationDistance;

                    SimulationDistance nearOnly =
                        new SimulationDistance(
                            peerDistance.NearSimulationDistance,
                            0,
                            peerDistance.IsClassic);

                    ZDOMan.instance.FindSectorObjects(
                        zone,
                        nearOnly,
                        _streamingNear);

                    _streamingReferencePosition =
                        position;

                    for (int j = 0;
                         j < _streamingNear.Count;
                         j++)
                    {
                        ZDO zdo =
                            _streamingNear[j];

                        if (zdo == null ||
                            !zdo.IsValid() ||
                            zdo.m_uid.IsNone() ||
                            !_streamingSeen.Add(zdo.m_uid))
                        {
                            continue;
                        }

                        if (ZNetScene.instance != null &&
                            !ZNetScene.instance.HasPrefab(
                                zdo.GetPrefab()))
                        {
                            continue;
                        }

                        if (ShouldDeferTerrainZdo(zdo))
                        {
                            continue;
                        }

                        _streamingCandidates.Add(zdo);
                    }

                    _streamingCandidates.Sort(
                        CompareStreamingDistance);

                    int limit =
                        Math.Min(
                            _streamingBoostMaxZdos.Value,
                            _streamingCandidates.Count);

                    for (int j = 0;
                         j < limit;
                         j++)
                    {
                        ZDOMan.instance.ForceSendZDO(
                            peer.m_uid,
                            _streamingCandidates[j].m_uid);
                    }

                    state.HasZone =
                        true;

                    state.Zone =
                        zone;

                    state.NextTime =
                        now +
                        _streamingBoostCooldown.Value;

                    _streamingPeerStates[peer.m_uid] =
                        state;
                }

                _streamingNear.Clear();
                _streamingCandidates.Clear();
                _streamingSeen.Clear();
            }
            catch (Exception ex)
            {
                if (!_streamingFailureLogged)
                {
                    _streamingFailureLogged =
                        true;

                    _log?.LogWarning(
                        "Bounded zone streaming boost disabled after an unexpected runtime shape: " +
                        ex.Message);
                }
            }
        }

        private static int CompareStreamingDistance(
            ZDO left,
            ZDO right)
        {
            Vector3 a =
                left.GetPosition() -
                _streamingReferencePosition;

            Vector3 b =
                right.GetPosition() -
                _streamingReferencePosition;

            float leftDistance =
                a.x * a.x +
                a.z * a.z;

            float rightDistance =
                b.x * b.x +
                b.z * b.z;

            return Utils.CompareFloats(
                leftDistance,
                rightDistance);
        }

        private static bool ShouldDeferTerrainZdo(
            ZDO zdo)
        {
            if (ZNetScene.instance == null)
            {
                return false;
            }

            try
            {
                GameObject prefab =
                    ZNetScene.instance.GetPrefab(
                        zdo.GetPrefab());

                if (prefab == null ||
                    prefab.GetComponent<TerrainComp>() == null &&
                    prefab.GetComponentInChildren<TerrainComp>(
                        true) == null)
                {
                    return false;
                }

                return
                    Heightmap.FindHeightmap(
                        zdo.GetPosition()) == null;
            }
            catch
            {
                return true;
            }
        }
    }
}