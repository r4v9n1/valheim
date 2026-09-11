using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightMyFire
{
    /// <summary>
    /// A LightMyFire coal/resin barrel. Owns its fuel inventory authoritatively: only this
    /// object's ZNetView owner ever mutates its Container inventory. All other peers must go
    /// through the RequestFuel/GrantFuel/AckApplied/RefundFuel RPC exchange below.
    ///
    /// The one-click build compiles this source against the target machine's currently installed
    /// Valheim/Jotunn assemblies, so API drift is caught by the build instead of being guessed.
    /// </summary>
    public sealed class LightMyFireBarrel : MonoBehaviour
    {
        // Requester (fireplace owner) -> barrel owner: "I need up to N fuel for fireplace F, transaction T."
        private const string RpcRequestFuel = "R4V9N1_LightMyFire_RequestFuel";
        // Requester -> barrel owner: "Transaction T succeeded, the fuel was applied, keep it consumed."
        private const string RpcAckApplied = "R4V9N1_LightMyFire_AckApplied";
        // Requester -> barrel owner: "Transaction T could not be applied, please return N fuel."
        private const string RpcRefundFuel = "R4V9N1_LightMyFire_RefundFuel";

        /// <summary>Absolute upper bound on any single request, regardless of what a fireplace claims to need.</summary>
        internal const int MaxRequestPerTransaction = 50;

        /// <summary>How long a barrel-side reservation waits for an Ack/Refund before auto-refunding.</summary>
        private const float ReservationTimeoutSeconds = 30f;

        private static readonly List<LightMyFireBarrel> ActiveBarrels = new List<LightMyFireBarrel>();

        private ZNetView _nview;
        private Container _container;
        private Inventory _inventory;
        [SerializeField]
        private string _fuelItemName;
        private bool _rpcRegistered;
        private bool _inventoryHooksRegistered;
        private bool _runtimeInitialized;
        private bool _sanitizingInventory;

        // Barrel-owner side bookkeeping: fuel already removed from the inventory and promised to a
        // requester, awaiting either AckApplied (consume permanently) or RefundFuel (return it).
        private readonly Dictionary<long, PendingReservation> _pendingReservations = new Dictionary<long, PendingReservation>();
        private float _nextReservationSweep;

        private struct PendingReservation
        {
            public int Amount;
            public float ReservedAtRealtime;
        }

        internal void Configure(string fuelItemName)
        {
            _fuelItemName = fuelItemName;
        }

        internal string FuelItemName
        {
            get { EnsureConfiguration(); return _fuelItemName; }
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
            EnsureConfiguration();
        }

        private void Start()
        {
            TryInitializeRuntime();
        }

        private void Update()
        {
            // ZNetView and this component are both on the cloned piece, and Unity does not guarantee
            // component Awake ordering. In 0.2.0 we tried exactly once in Awake and permanently gave
            // up when ZNetView had not created/attached its ZDO yet. The barrel would still open and
            // store fuel, but it never entered ActiveBarrels and therefore could never feed a light.
            // Retry until the network object is actually ready, then this becomes a no-op forever.
            if (!_runtimeInitialized)
            {
                TryInitializeRuntime();
            }
        }

        private bool TryInitializeRuntime()
        {
            EnsureConfiguration();

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }
            if (_container == null)
            {
                _container = GetComponent<Container>();
            }
            if (_container == null)
            {
                return false;
            }

            if (_inventory == null)
            {
                _inventory = _container.GetInventory();
            }
            if (_inventory != null && !_inventoryHooksRegistered)
            {
                _inventory.m_onChanged += OnInventoryChanged;
                LightMyFireItemFilter.RegisterFilteredInventory(_inventory, _fuelItemName);
                _inventoryHooksRegistered = true;
            }


            if (_nview == null || !_nview.IsValid() || _nview.GetZDO() == null || _inventory == null)
            {
                return false;
            }

            if (!_rpcRegistered)
            {
                _nview.Register<long, ZDOID, int>(RpcRequestFuel, RPC_RequestFuel);
                _nview.Register<long>(RpcAckApplied, RPC_AckApplied);
                _nview.Register<long, int>(RpcRefundFuel, RPC_RefundFuel);
                _rpcRegistered = true;
            }

            if (!ActiveBarrels.Contains(this))
            {
                ActiveBarrels.Add(this);
            }

            _runtimeInitialized = true;
            LightMyFirePlugin.LogBarrelRuntimeReady(gameObject.name, _fuelItemName, GetFuelCount());
            return true;
        }

        private void EnsureConfiguration()
        {
            if (!string.IsNullOrEmpty(_fuelItemName))
            {
                return;
            }

            bool isResin = gameObject.name.IndexOf("ResinBarrel", StringComparison.OrdinalIgnoreCase) >= 0;
            _fuelItemName = isResin ? LightMyFirePlugin.ResinItemName : LightMyFirePlugin.CoalItemName;
        }

        private void OnDestroy()
        {
            if (_inventory != null && _inventoryHooksRegistered)
            {
                _inventory.m_onChanged -= OnInventoryChanged;
                LightMyFireItemFilter.UnregisterFilteredInventory(_inventory);
                _inventoryHooksRegistered = false;
            }
            _inventory = null;
            _runtimeInitialized = false;
            _rpcRegistered = false;

            // Any fuel we had reserved-but-not-yet-consumed for outstanding transactions is about
            // to become unrecoverable (the object is unloading/being destroyed). We already removed
            // it from the visible inventory when we reserved it, so nothing further to do here other
            // than stop tracking it - this matches "never silently duplicate", not "never lose under
            // an ownership/unload race", which the design accepts as a bounded, documented risk.
            _pendingReservations.Clear();
            ActiveBarrels.Remove(this);
        }

        private void OnInventoryChanged()
        {
            // Normal UI insertion is blocked before it happens by the Harmony gate. This immediate
            // owner-side sanitation is the second line of defence for direct inventory mutations
            // performed by another mod or by an unpatched future Valheim insertion path.
            if (!_sanitizingInventory)
            {
                EjectInvalidItems();
            }
        }


        /// <summary>Owner-side safety sweep: catches any item that should never have entered this
        /// barrel (e.g. inserted by another mod bypassing the Harmony filter) and ejects it into the
        /// world next to the barrel rather than silently deleting it.</summary>
        internal void EjectInvalidItems()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || _inventory == null)
            {
                return;
            }

            EnsureConfiguration();
            List<ItemDrop.ItemData> items = _inventory.GetAllItems();
            if (items == null || items.Count == 0)
            {
                return;
            }

            // Copy first: RemoveItem mutates the same list GetAllItems() returned.
            List<ItemDrop.ItemData> invalid = null;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null)
                {
                    continue;
                }

                if (!string.Equals(item.m_shared.m_name, _fuelItemName, StringComparison.Ordinal))
                {
                    if (invalid == null)
                    {
                        invalid = new List<ItemDrop.ItemData>();
                    }
                    invalid.Add(item);
                }
            }

            if (invalid == null)
            {
                return;
            }

            _sanitizingInventory = true;
            try
            {
                for (int i = 0; i < invalid.Count; i++)
                {
                    ItemDrop.ItemData item = invalid[i];
                    _inventory.RemoveItem(item);
                    ItemDrop.DropItem(item, item.m_stack, transform.position + Vector3.up * 0.5f, transform.rotation);
                }
            }
            finally
            {
                _sanitizingInventory = false;
            }

            LightMyFirePlugin.LogInvalidItemEjection(invalid.Count, gameObject.name);
        }

        internal void SweepStaleReservations()
        {
            if (_pendingReservations.Count == 0 || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextReservationSweep)
            {
                return;
            }
            _nextReservationSweep = now + 5f;

            List<long> expired = null;
            foreach (KeyValuePair<long, PendingReservation> entry in _pendingReservations)
            {
                if (now - entry.Value.ReservedAtRealtime >= ReservationTimeoutSeconds)
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
                long txId = expired[i];
                PendingReservation reservation;
                if (_pendingReservations.TryGetValue(txId, out reservation))
                {
                    _pendingReservations.Remove(txId);
                    RefundLocally(reservation.Amount);
                }
            }
        }

        private bool CanReceiveRequest(Vector3 targetPosition, float rangeSqr)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return false;
            }

            if (!_runtimeInitialized && !TryInitializeRuntime())
            {
                return false;
            }

            if (_nview == null || !_nview.IsValid() || !_nview.HasOwner() || _container == null)
            {
                return false;
            }

            return (transform.position - targetPosition).sqrMagnitude <= rangeSqr;
        }

        internal int GetFuelCount()
        {
            EnsureConfiguration();
            if (_inventory == null && _container != null)
            {
                _inventory = _container.GetInventory();
            }
            return _inventory == null ? 0 : _inventory.CountItems(_fuelItemName, -1, false);
        }

        /// <summary>Removes fuel from the inventory without going through the filter (same fuel type, always allowed).</summary>
        private int RemoveFuelLocally(int requested)
        {
            if (requested <= 0 || _inventory == null)
            {
                return 0;
            }

            int before = _inventory.CountItems(_fuelItemName, -1, false);
            int toRemove = Math.Min(before, requested);
            if (toRemove <= 0)
            {
                return 0;
            }

            _inventory.RemoveItem(_fuelItemName, toRemove, -1, false);
            int after = _inventory.CountItems(_fuelItemName, -1, false);
            return Mathf.Clamp(before - after, 0, toRemove);
        }

        private void RefundLocally(int amount)
        {
            if (amount <= 0 || _inventory == null)
            {
                return;
            }

            EnsureConfiguration();
            // Valheim 1.0 added explicit cheated/pickedUp flags to this helper.
            // Keep both false so a refund remains an ordinary inventory item.
            _inventory.AddItem(_fuelItemName, amount, 1, 0, 0L, string.Empty, false, false);
        }

        private void RPC_RequestFuel(long sender, long transactionId, ZDOID fireplaceId, int requestedAmount)
        {
            if (!LightMyFirePlugin.IsEnabled() || _nview == null || !_nview.IsValid() || !_nview.IsOwner() || _inventory == null)
            {
                return;
            }

            if (_pendingReservations.ContainsKey(transactionId))
            {
                // Duplicate delivery of the same request: do not reserve fuel twice.
                return;
            }

            int clampedRequest = Mathf.Clamp(requestedAmount, 0, MaxRequestPerTransaction);
            if (clampedRequest <= 0)
            {
                return;
            }

            EnsureConfiguration();
            int granted = RemoveFuelLocally(clampedRequest);
            if (granted <= 0)
            {
                LightMyFireFeeder.SendGrant(transactionId, GetZdoId(), fireplaceId, 0);
                return;
            }

            _pendingReservations[transactionId] = new PendingReservation
            {
                Amount = granted,
                ReservedAtRealtime = Time.realtimeSinceStartup
            };

            LightMyFireFeeder.SendGrant(transactionId, GetZdoId(), fireplaceId, granted);
        }

        private void RPC_AckApplied(long sender, long transactionId)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            // Fuel was successfully applied by the requester; the reservation is now permanently
            // consumed. Nothing to add back - just stop tracking it.
            _pendingReservations.Remove(transactionId);
        }

        private void RPC_RefundFuel(long sender, long transactionId, int amount)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
            {
                return;
            }

            PendingReservation reservation;
            if (!_pendingReservations.TryGetValue(transactionId, out reservation))
            {
                // Unknown/already-resolved transaction: ignore rather than trust an arbitrary refund
                // amount from the network.
                return;
            }

            _pendingReservations.Remove(transactionId);
            int refundAmount = Mathf.Clamp(amount, 0, reservation.Amount);
            RefundLocally(refundAmount);
        }

        internal ZDOID GetZdoId()
        {
            ZDO zdo = _nview == null ? null : _nview.GetZDO();
            return zdo == null ? ZDOID.None : zdo.m_uid;
        }

        internal void SendRequestFuel(long transactionId, ZDOID fireplaceId, int requestedAmount)
        {
            if (_nview != null && _nview.IsValid() && _nview.HasOwner())
            {
                _nview.InvokeRPC(RpcRequestFuel, transactionId, fireplaceId, Mathf.Clamp(requestedAmount, 0, MaxRequestPerTransaction));
            }
        }

        internal void SendAckApplied(long transactionId)
        {
            if (_nview != null && _nview.IsValid() && _nview.HasOwner())
            {
                _nview.InvokeRPC(RpcAckApplied, transactionId);
            }
        }

        internal void SendRefundFuel(long transactionId, int amount)
        {
            if (_nview != null && _nview.IsValid() && _nview.HasOwner() && amount > 0)
            {
                _nview.InvokeRPC(RpcRefundFuel, transactionId, amount);
            }
        }

        internal static LightMyFireBarrel FindByZdoId(ZDOID id)
        {
            if (id == ZDOID.None)
            {
                return null;
            }

            for (int i = ActiveBarrels.Count - 1; i >= 0; i--)
            {
                LightMyFireBarrel barrel = ActiveBarrels[i];
                if (barrel == null)
                {
                    ActiveBarrels.RemoveAt(i);
                    continue;
                }

                if (barrel.GetZdoId() == id)
                {
                    return barrel;
                }
            }

            return null;
        }

        internal static bool TryFindActiveMatchingBarrels(string fuelItemName, List<LightMyFireBarrel> results)
        {
            results.Clear();
            for (int i = ActiveBarrels.Count - 1; i >= 0; i--)
            {
                LightMyFireBarrel barrel = ActiveBarrels[i];
                if (barrel == null)
                {
                    ActiveBarrels.RemoveAt(i);
                    continue;
                }

                barrel.EnsureConfiguration();
                if (string.Equals(barrel._fuelItemName, fuelItemName, StringComparison.Ordinal))
                {
                    results.Add(barrel);
                }
            }

            return results.Count > 0;
        }

        internal bool IsWithinRange(Vector3 targetPosition, float rangeSqr)
        {
            return CanReceiveRequest(targetPosition, rangeSqr);
        }

        internal static bool HasActiveBarrels()
        {
            for (int i = ActiveBarrels.Count - 1; i >= 0; i--)
            {
                if (ActiveBarrels[i] == null)
                {
                    ActiveBarrels.RemoveAt(i);
                }
            }

            // Recovery path for old saves / unusual component ordering: if the registry is empty,
            // discover loaded barrel instances and let them finish initialization now that the scene
            // and ZNetViews are established. This is only paid while the registry is empty.
            if (ActiveBarrels.Count == 0)
            {
                LightMyFireBarrel[] loaded = UnityEngine.Object.FindObjectsByType<LightMyFireBarrel>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < loaded.Length; i++)
                {
                    LightMyFireBarrel barrel = loaded[i];
                    if (barrel != null)
                    {
                        barrel.TryInitializeRuntime();
                    }
                }
            }

            return ActiveBarrels.Count > 0;
        }

        internal static void TickMaintenance()
        {
            for (int i = ActiveBarrels.Count - 1; i >= 0; i--)
            {
                LightMyFireBarrel barrel = ActiveBarrels[i];
                if (barrel == null)
                {
                    ActiveBarrels.RemoveAt(i);
                    continue;
                }

                barrel.SweepStaleReservations();
            }
        }

        internal static void EjectInvalidItemsFromOwnedBarrels()
        {
            for (int i = ActiveBarrels.Count - 1; i >= 0; i--)
            {
                LightMyFireBarrel barrel = ActiveBarrels[i];
                if (barrel == null)
                {
                    ActiveBarrels.RemoveAt(i);
                    continue;
                }

                barrel.EjectInvalidItems();
            }
        }
    }
}
