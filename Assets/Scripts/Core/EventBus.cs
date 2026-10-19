using System;
using System.Collections.Generic;

namespace LustHouse.Core
{
    /// <summary>
    /// Minimal typed event bus decoupling UI from systems (plan §5.2).
    ///
    /// Two defects in the original stub are fixed here:
    ///  * Unsubscribe actually removes the handler (it used <c>RemoveAll(o =&gt; false)</c>).
    ///  * Publish iterates a snapshot, so a handler may safely subscribe or unsubscribe
    ///    during dispatch without invalidating the iteration or throwing.
    /// </summary>
    public static class EventBus
    {
        private sealed class Subscription
        {
            public readonly Action<object> Wrapper;
            public readonly Delegate Original;

            public Subscription(Action<object> wrapper, Delegate original)
            {
                Wrapper = wrapper;
                Original = original;
            }
        }

        private static readonly Dictionary<Type, List<Subscription>> _handlers =
            new Dictionary<Type, List<Subscription>>();

        public static void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
            {
                list = new List<Subscription>();
                _handlers[t] = list;
            }
            list.Add(new Subscription(o => handler((T)o), handler));
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) return;
            if (!_handlers.TryGetValue(typeof(T), out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Original.Equals(handler)) list.RemoveAt(i);
            }
        }

        public static void Publish<T>(T payload)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;

            // Snapshot: handlers may mutate the subscription list while we dispatch.
            var snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i].Wrapper(payload);
        }

        /// <summary>Editor/test helper. Drops every subscription.</summary>
        public static void Clear()
        {
            _handlers.Clear();
        }

        public static int SubscriberCount<T>() =>
            _handlers.TryGetValue(typeof(T), out var list) ? list.Count : 0;
    }

    // ---- Payloads -----------------------------------------------------------

    public struct WalletChanged
    {
        public Currency currency;
        public long balance;
        public long delta;
        public LedgerReason reason;
    }

    public struct WalletConversionBlocked
    {
        public string reason;
    }

    public struct InventoryEquipped { public string itemId; public string category; }
    public struct InventoryItemAdded { public string itemId; public string category; }
    public struct InventoryPurchaseFailed { public string itemId; public string reason; }

    public struct LevelUp { public int newLevel; public long xpReward; }
    public struct VipTierChanged { public int newTier; public string tierName; }

    public struct DailyGiftReady { }
    public struct DailyGiftClaimed { public int streakDay; public long coins; public long gems; }

    public struct MissionProgressed { public string missionId; public int progress; public int target; }
    public struct MissionClaimable { public string missionId; }

    public struct AvatarChanged { public string loadoutHash; }

    /// <summary>
    /// Raised when the save could not be read or migrated. The lobby surfaces this as a
    /// non-blocking warn chip (plan §2.13) — never a modal.
    /// </summary>
    public struct SaveRecovered { public string warning; }
}