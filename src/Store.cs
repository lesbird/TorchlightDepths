using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Dungeon
{
    /// <summary>One gold pack on sale. Ids must match the consumable products in App Store Connect.</summary>
    public sealed record GoldPack(string Id, int Gold, bool DoubleHp, string Label);

    public static class GoldPacks
    {
        public static readonly GoldPack[] All =
        {
            new("com.lesbird.torchlightdepths.gold500", 500, false, "500 GOLD"),
            new("com.lesbird.torchlightdepths.gold2000", 2000, false, "2000 GOLD"),
            new("com.lesbird.torchlightdepths.gold5000", 5000, false, "5000 GOLD"),
            new("com.lesbird.torchlightdepths.gold10000", 10000, true, "10000 GOLD + DOUBLE HP"),
        };

        public static GoldPack Find(string id) => Array.Find(All, p => p.Id == id);
    }

    public enum StoreState { Loading, Ready, Unavailable }
    public enum StoreEventKind { Purchased, Failed, Cancelled, Deferred }

    /// <summary>
    /// A purchase outcome. For <see cref="StoreEventKind.Purchased"/> the game credits the
    /// pack, persists it, then hands <see cref="Token"/> back to <see cref="IStore.Finish"/>.
    /// </summary>
    public readonly record struct StoreEvent(StoreEventKind Kind, string ProductId, string TransactionId, object Token, string Message);

    /// <summary>Platform storefront (StoreKit on iOS). The game polls it from its own thread.</summary>
    public interface IStore
    {
        StoreState State { get; }
        /// <summary>Localized price, already limited to characters the HUD font can draw; null if unknown.</summary>
        string Price(string productId);
        /// <summary>Try loading the products again after a failure.</summary>
        void Refresh();
        void Buy(string productId);
        bool TryTakeEvent(out StoreEvent e);
        /// <summary>Tell the store a purchase has been delivered so it stops redelivering it.</summary>
        void Finish(object token);
    }

    /// <summary>
    /// Stand-in store for the simulator and capture runs: every pack is for sale and
    /// purchases succeed after a short pause, as if the App Store sheet was approved.
    /// </summary>
    public sealed class FakeStore : IStore
    {
        readonly ConcurrentQueue<(long due, StoreEvent e)> _queue = new();
        int _next;

        public StoreState State => StoreState.Ready;

        public string Price(string productId) => GoldPacks.Find(productId)?.Gold switch
        {
            500 => "$0.99", 2000 => "$1.99", 5000 => "$2.99", 10000 => "$4.99", _ => null,
        };

        public void Refresh() { }

        public void Buy(string productId)
        {
            var e = new StoreEvent(StoreEventKind.Purchased, productId, $"fake-{++_next}", null, null);
            _queue.Enqueue((Stopwatch.GetTimestamp() + Stopwatch.Frequency, e)); // ~1 s "sheet"
        }

        public bool TryTakeEvent(out StoreEvent e)
        {
            if (_queue.TryPeek(out var head) && head.due <= Stopwatch.GetTimestamp() && _queue.TryDequeue(out head))
            {
                e = head.e;
                return true;
            }
            e = default;
            return false;
        }

        public void Finish(object token) { }
    }

    /// <summary>
    /// Purchases that haven't reached a hero yet. A pack bought from the title menu or the
    /// death screen waits here until a hero is alive in the dungeon (or revives), so a
    /// purchase is never lost to a NEW HERO. Kept apart from the run save, which is
    /// deleted on death.
    /// </summary>
    public sealed class WalletData
    {
        public int Gold { get; set; }
        public int HpDoubles { get; set; }
        /// <summary>Recently credited transaction ids, so a redelivered purchase isn't paid twice.</summary>
        public List<string> Credited { get; set; } = new();

        public bool IsEmpty => Gold == 0 && HpDoubles == 0;
    }
}
