using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMyFire
{
    /// <summary>
    /// Fireplace-owner side of the refill protocol. A peer only acts here for Fireplace objects it
    /// currently owns (nview.IsOwner()), so it never mutates a fireplace/light it does not own, and
    /// it never mutates a barrel directly - it only ever asks the barrel's owner to do so.
    /// </summary>
    internal static class LightMyFireFeeder
    {
        internal const string RpcGrantFuel = "R4V9N1_LightMyFire_GrantFuel";

        private const float OutboundRequestTimeoutSeconds = 20f;

        private struct OutboundState
        {
            public LightMyFireBarrel Barrel;
            public ZDOID FireplaceId;
            public int RequestedAmount;
            public float SentAtRealtime;
        }

        private static readonly Dictionary<long, OutboundState> PendingOutbound = new Dictionary<long, OutboundState>();
        // Fireplaces with at least one unresolved outstanding request, so we don't pile up duplicate
        // concurrent requests for the same light across successive scan ticks.
        private static readonly HashSet<ZDOID> FireplacesAwaitingResponse = new HashSet<ZDOID>();

        private static long _txCounter;

        private static long NextTransactionId()
        {
            long uid = unchecked((long)ZNet.GetUID());
            _txCounter++;
            // Best-effort uniqueness: high bits from this peer's network UID, low bits from a local
            // monotonically increasing counter. Collisions across peers are extremely unlikely and,
            // if they ever happened, would only cause one spurious duplicate-transaction rejection
            // (handled safely on the barrel side) rather than any fuel loss or duplication.
            return unchecked((uid << 20) ^ (_txCounter & 0xFFFFF));
        }

        internal static void PruneStaleOutbound()
        {
            if (PendingOutbound.Count == 0)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            List<long> expired = null;
            foreach (KeyValuePair<long, OutboundState> entry in PendingOutbound)
            {
                if (now - entry.Value.SentAtRealtime >= OutboundRequestTimeoutSeconds)
                {
                    if (expired == null)
                    {
                        expired = new List<long>();
                    }
                    expired.Add(entry.Key);
                }
            }

            if (expired == null)
            {
                return;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                OutboundState state;
                if (PendingOutbound.TryGetValue(expired[i], out state))
                {
                    PendingOutbound.Remove(expired[i]);
                    FireplacesAwaitingResponse.Remove(state.FireplaceId);
                    // We never took fuel on this side, so there is nothing to refund here. The
                    // barrel-side reservation (if the request did arrive) is refunded independently
                    // by LightMyFireBarrel.SweepStaleReservations() after its own timeout.
                }
            }
        }

        /// <summary>Runs once per configured refill interval. Services every loaded, owned Fireplace
        /// (vanilla or modded) whose fuel item is vanilla Coal or Resin - never a hardcoded prefab list.</summary>
        internal static void ScanAndRequestRefills()
        {
            PruneStaleOutbound();

            if (!LightMyFireBarrel.HasActiveBarrels())
            {
                return;
            }

            Fireplace[] fireplaces = UnityEngine.Object.FindObjectsByType<Fireplace>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (fireplaces == null || fireplaces.Length == 0)
            {
                LightMyFirePlugin.LogScanSummary(0, 0, 0, 0, 0, 0);
                return;
            }

            int ownedFireplaces = 0;
            int compatibleFireplaces = 0;
            int underFueledFireplaces = 0;
            int inRangeMatches = 0;
            int requestsSent = 0;

            float range = LightMyFirePlugin.GetFeedRange();
            float rangeSqr = range * range;
            var candidateBuffer = new List<LightMyFireBarrel>();
            var sortedCandidates = new List<BarrelDistance>();

            for (int i = 0; i < fireplaces.Length; i++)
            {
                Fireplace fireplace = fireplaces[i];
                if (fireplace == null)
                {
                    continue;
                }

                ZNetView nview = ResolveNetworkView(fireplace);
                if (nview == null || !nview.IsValid() || !nview.IsOwner())
                {
                    continue;
                }
                ownedFireplaces++;

                ZDO zdo = nview.GetZDO();
                if (zdo == null)
                {
                    continue;
                }

                ZDOID fireplaceId = zdo.m_uid;
                if (FireplacesAwaitingResponse.Contains(fireplaceId))
                {
                    continue;
                }

                string fuelItemName = LightMyFirePlugin.GetSupportedFuelItemName(fireplace);
                if (fuelItemName == null)
                {
                    continue;
                }
                compatibleFireplaces++;

                float currentFuel = zdo.GetFloat(ZDOVars.s_fuel);
                float maxFuel = fireplace.m_maxFuel;
                int missing = Mathf.FloorToInt(maxFuel - currentFuel + 0.001f);
                if (missing <= 0)
                {
                    continue;
                }
                underFueledFireplaces++;

                if (!LightMyFireBarrel.TryFindActiveMatchingBarrels(fuelItemName, candidateBuffer))
                {
                    continue;
                }

                Vector3 firePos = fireplace.transform.position;
                sortedCandidates.Clear();
                for (int b = 0; b < candidateBuffer.Count; b++)
                {
                    LightMyFireBarrel barrel = candidateBuffer[b];
                    if (!barrel.IsWithinRange(firePos, rangeSqr))
                    {
                        continue;
                    }

                    // Do NOT use GetFuelCount() here unless this peer owns the barrel. Container
                    // inventories are authoritative on the barrel owner, so a remote peer may see a
                    // stale/empty local copy even while the real barrel still contains plenty of fuel.
                    // The RequestFuel RPC is deliberately authoritative: the barrel owner decides
                    // how much can actually be granted.
                    sortedCandidates.Add(new BarrelDistance(barrel, (barrel.transform.position - firePos).sqrMagnitude));
                }

                if (sortedCandidates.Count == 0)
                {
                    continue;
                }
                inRangeMatches++;

                sortedCandidates.Sort(delegate (BarrelDistance left, BarrelDistance right)
                {
                    return left.DistanceSqr.CompareTo(right.DistanceSqr);
                });

                // Request from the nearest matching barrel. We intentionally do not pre-judge its
                // inventory from this peer's local Container copy. The barrel owner clamps the grant
                // to its real authoritative inventory. This fixes dedicated-server/multiplayer cases
                // where some lights were skipped simply because they were owned by a different peer
                // than the barrel.
                LightMyFireBarrel selectedBarrel = sortedCandidates[0].Barrel;
                int take = Mathf.Min(missing, LightMyFireBarrel.MaxRequestPerTransaction);
                if (take > 0)
                {
                    long txId = NextTransactionId();
                    PendingOutbound[txId] = new OutboundState
                    {
                        Barrel = selectedBarrel,
                        FireplaceId = fireplaceId,
                        RequestedAmount = take,
                        SentAtRealtime = Time.realtimeSinceStartup
                    };
                    FireplacesAwaitingResponse.Add(fireplaceId);
                    selectedBarrel.SendRequestFuel(txId, fireplaceId, take);
                    requestsSent++;
                }
            }

            LightMyFirePlugin.LogScanSummary(fireplaces.Length, ownedFireplaces, compatibleFireplaces, underFueledFireplaces, inRangeMatches, requestsSent);
        }

        /// <summary>Called by a barrel's owner to notify the fireplace's owner of a grant/decline.
        /// Routed by raw peer ID (via ZDO ownership), not through a local ZNetView, because the
        /// barrel owner may not have the target fireplace's GameObject instantiated locally even
        /// though it is close enough to be in range - only the ZDO ownership record needs to be
        /// known here, not the full object.</summary>
        internal static void SendGrant(long transactionId, ZDOID barrelId, ZDOID fireplaceId, int grantedAmount)
        {
            if (ZRoutedRpc.instance == null || ZDOMan.instance == null)
            {
                return;
            }

            ZDO fireplaceZdo = ZDOMan.instance.GetZDO(fireplaceId);
            if (fireplaceZdo == null)
            {
                return;
            }

            long targetPeer = fireplaceZdo.GetOwner();
            if (targetPeer == 0L)
            {
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer, RpcGrantFuel, transactionId, barrelId, fireplaceId, grantedAmount);
        }

        /// <summary>Handles the barrel owner's response. Only meaningful if we are still the owner of
        /// the target fireplace; otherwise we safely refund so no fuel is stranded or duplicated.</summary>
        internal static void RPC_GrantFuel(long sender, long transactionId, ZDOID barrelId, ZDOID fireplaceId, int grantedAmount)
        {
            OutboundState state;
            LightMyFireBarrel barrel = null;
            bool hadState = PendingOutbound.TryGetValue(transactionId, out state);
            if (hadState)
            {
                PendingOutbound.Remove(transactionId);
                FireplacesAwaitingResponse.Remove(state.FireplaceId);
                barrel = state.Barrel;
            }
            else
            {
                barrel = LightMyFireBarrel.FindByZdoId(barrelId);
            }

            if (grantedAmount <= 0)
            {
                return;
            }

            if (!hadState)
            {
                // Unknown or already-timed-out transaction: we can't safely apply this fuel to
                // anything, so return it to the barrel rather than let it vanish.
                RefundToBarrel(barrel, transactionId, grantedAmount);
                return;
            }

            int appliedAmount = TryApplyFuelToFireplace(fireplaceId, grantedAmount);
            int leftover = grantedAmount - appliedAmount;
            if (leftover > 0)
            {
                RefundToBarrel(barrel, transactionId, leftover);
            }
            else if (barrel != null)
            {
                barrel.SendAckApplied(transactionId);
            }
        }

        private static void RefundToBarrel(LightMyFireBarrel barrel, long transactionId, int amount)
        {
            if (barrel != null && amount > 0)
            {
                barrel.SendRefundFuel(transactionId, amount);
            }
        }

        private static int TryApplyFuelToFireplace(ZDOID fireplaceId, int grantedAmount)
        {
            if (ZNetScene.instance == null)
            {
                return 0;
            }

            GameObject targetObject = ZNetScene.instance.FindInstance(fireplaceId);
            if (targetObject == null)
            {
                return 0;
            }

            Fireplace fireplace = targetObject.GetComponent<Fireplace>();
            if (fireplace == null)
            {
                fireplace = targetObject.GetComponentInChildren<Fireplace>(true);
            }
            ZNetView fireplaceView = ResolveNetworkView(fireplace);
            if (fireplace == null || fireplaceView == null || !fireplaceView.IsValid() || !fireplaceView.IsOwner())
            {
                return 0;
            }

            ZDO zdo = fireplaceView.GetZDO();
            if (zdo == null)
            {
                return 0;
            }

            float currentFuel = zdo.GetFloat(ZDOVars.s_fuel);
            int actualMissing = Mathf.Clamp(Mathf.FloorToInt(fireplace.m_maxFuel - currentFuel + 0.001f), 0, LightMyFireBarrel.MaxRequestPerTransaction);
            int applied = Mathf.Min(grantedAmount, actualMissing);
            if (applied <= 0)
            {
                return 0;
            }

            fireplace.AddFuel(applied);
            LightMyFirePlugin.LogTransfer(applied, LightMyFirePlugin.GetSupportedFuelItemName(fireplace), fireplace);
            return applied;
        }

        private static ZNetView ResolveNetworkView(Fireplace fireplace)
        {
            if (fireplace == null)
            {
                return null;
            }

            ZNetView view = fireplace.GetComponent<ZNetView>();
            if (view != null)
            {
                return view;
            }

            // Several Valheim/modded light prefabs keep Fireplace on a child while the network view
            // lives on the prefab root. GetComponent-only silently skipped those source types.
            view = fireplace.GetComponentInParent<ZNetView>();
            if (view != null)
            {
                return view;
            }

            return fireplace.GetComponentInChildren<ZNetView>(true);
        }

        private readonly struct BarrelDistance
        {
            public readonly LightMyFireBarrel Barrel;
            public readonly float DistanceSqr;

            public BarrelDistance(LightMyFireBarrel barrel, float distanceSqr)
            {
                Barrel = barrel;
                DistanceSqr = distanceSqr;
            }
        }
    }
}
