using System;
using System.Collections.Generic;

namespace LustHouse.Core
{
    public static class EventBus
    {
        private static readonly Dictionary<Type, List<Action<object>>> _handlers
            = new Dictionary<Type, List<Action<object>>>();

        public static void Subscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (!_handlers.TryGetValue(t, out var list))
            {
                list = new List<Action<object>>();
                _handlers[t] = list;
            }
            list.Add(o => handler((T)o));
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (_handlers.TryGetValue(t, out var list))
                list.RemoveAll(o => false); // placeholder; use typed removal in prod
        }

        public static void Publish<T>(T payload)
        {
            var t = typeof(T);
            if (_handlers.TryGetValue(t, out var list))
                foreach (var h in list) h(payload);
        }
    }

    public struct WalletChanged { public Currency currency; public long balance; }
    public struct InventoryEquipped { public string itemId; }
    public struct InventoryItemAdded { public string itemId; }
    public struct LevelUp { public int newLevel; }
    public struct VipTierChanged { public int newTier; }
    public struct DailyGiftReady { }
    public struct MissionProgressed { public string missionId; public int progress; public int target; }
    public struct MissionClaimable { public string missionId; }
}