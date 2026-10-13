using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using StoreKit;

namespace Dungeon
{
    /// <summary>
    /// <see cref="IStore"/> on StoreKit (the original API; StoreKit 2 is Swift-only). Products
    /// are the consumable gold packs in <see cref="GoldPacks"/>. The transaction observer is
    /// registered at launch so purchases interrupted by a crash or kill are redelivered, and
    /// a transaction is only finished after the game has saved the gold.
    /// </summary>
    public sealed class AppStoreKit : SKPaymentTransactionObserver, IStore
    {
        readonly ConcurrentQueue<StoreEvent> _events = new();
        readonly Dictionary<string, SKProduct> _products = new();   // main thread only
        readonly Dictionary<string, string> _prices = new();        // main thread only
        SKProductsRequest _request;
        ProductsDelegate _delegate;

        public StoreState State { get; private set; } = StoreState.Loading;

        public AppStoreKit()
        {
            SKPaymentQueue.DefaultQueue.AddTransactionObserver(this);
            Refresh();
        }

        public void Refresh()
        {
            if (_request != null) return; // already asking
            State = StoreState.Loading;
            var ids = new NSSet<NSString>(GoldPacks.All.Select(p => new NSString(p.Id)).ToArray());
            _delegate = new ProductsDelegate(this);
            _request = new SKProductsRequest(ids) { Delegate = _delegate };
            _request.Start();
        }

        public string Price(string productId) => _prices.TryGetValue(productId, out var p) ? p : null;

        public void Buy(string productId)
        {
            if (!SKPaymentQueue.CanMakePayments)
            {
                _events.Enqueue(new StoreEvent(StoreEventKind.Failed, productId, null, null, "PURCHASES ARE TURNED OFF"));
                return;
            }
            if (!_products.TryGetValue(productId, out var product))
            {
                _events.Enqueue(new StoreEvent(StoreEventKind.Failed, productId, null, null, "STORE UNAVAILABLE - TRY AGAIN LATER"));
                return;
            }
            SKPaymentQueue.DefaultQueue.AddPayment(SKPayment.CreateFrom(product));
        }

        public bool TryTakeEvent(out StoreEvent e) => _events.TryDequeue(out e);

        public void Finish(object token)
        {
            if (token is SKPaymentTransaction t) SKPaymentQueue.DefaultQueue.FinishTransaction(t);
        }

        // ---- product lookup ----------------------------------------------------------
        void OnProducts(SKProduct[] products) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            foreach (var p in products)
            {
                _products[p.ProductIdentifier] = p;
                _prices[p.ProductIdentifier] = FormatPrice(p);
            }
            _request = null;
            State = _products.Count > 0 ? StoreState.Ready : StoreState.Unavailable;
        });

        void OnFailed() => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            _request = null;
            State = _products.Count > 0 ? StoreState.Ready : StoreState.Unavailable;
        });

        // The localized price, e.g. "$0.99". The pixel font has no glyphs for symbols like
        // € or ¥, so those fall back to the number plus the ISO currency code ("0,99 EUR").
        static string FormatPrice(SKProduct p)
        {
            var f = new NSNumberFormatter { NumberStyle = NSNumberFormatterStyle.Currency, Locale = p.PriceLocale };
            string s = Clean(f.StringFromNumber(p.Price));
            if (s.All(Font.CanDraw)) return s;

            var plain = new NSNumberFormatter
            {
                NumberStyle = NSNumberFormatterStyle.Decimal, Locale = p.PriceLocale,
                MinimumFractionDigits = f.MinimumFractionDigits, MaximumFractionDigits = f.MaximumFractionDigits,
            };
            s = $"{Clean(plain.StringFromNumber(p.Price))} {p.PriceLocale.CurrencyCode}".ToUpperInvariant();
            return new string(s.Where(Font.CanDraw).ToArray()).Trim();
        }

        // Non-breaking and narrow spaces become plain spaces; letters go uppercase.
        static string Clean(string s) => new string((s ?? "").Select(c => char.IsWhiteSpace(c) ? ' ' : char.ToUpperInvariant(c)).ToArray()).Trim();

        sealed class ProductsDelegate : SKProductsRequestDelegate
        {
            readonly AppStoreKit _store;
            public ProductsDelegate(AppStoreKit store) => _store = store;

            public override void ReceivedResponse(SKProductsRequest request, SKProductsResponse response)
            {
                if (response.InvalidProducts.Length > 0)
                    Console.WriteLine($"[store] not found in App Store Connect: {string.Join(", ", response.InvalidProducts)}");
                _store.OnProducts(response.Products);
            }

            public override void RequestFailed(SKRequest request, NSError error)
            {
                Console.WriteLine($"[store] product request failed: {error?.LocalizedDescription}");
                _store.OnFailed();
            }
        }

        // ---- transactions ------------------------------------------------------------
        public override void UpdatedTransactions(SKPaymentQueue queue, SKPaymentTransaction[] transactions)
        {
            foreach (var t in transactions)
            {
                string id = t.Payment?.ProductIdentifier;
                switch (t.TransactionState)
                {
                    case SKPaymentTransactionState.Purchased:
                        // finished later, by Finish(), once the game has saved the gold
                        _events.Enqueue(new StoreEvent(StoreEventKind.Purchased, id, t.TransactionIdentifier, t, null));
                        break;
                    case SKPaymentTransactionState.Failed:
                        bool cancelled = t.Error != null && t.Error.Code == (nint)(long)SKError.PaymentCancelled;
                        _events.Enqueue(new StoreEvent(cancelled ? StoreEventKind.Cancelled : StoreEventKind.Failed, id, null, null,
                            cancelled ? null : "PURCHASE FAILED"));
                        queue.FinishTransaction(t);
                        break;
                    case SKPaymentTransactionState.Deferred: // Ask to Buy: a parent has to approve it
                        _events.Enqueue(new StoreEvent(StoreEventKind.Deferred, id, null, null, null));
                        break;
                    case SKPaymentTransactionState.Restored: // consumables are never restored
                        queue.FinishTransaction(t);
                        break;
                }
            }
        }
    }
}
