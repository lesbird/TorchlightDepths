using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace Dungeon
{
    // The gold shop: in-app purchases of gold packs (iOS only; the desktop build has no store).
    // Reached from the title menu (BUY GOLD) and the death screen. Purchases land in the
    // wallet first and reach the hero as soon as one is alive in the dungeon, or revives.
    public sealed partial class Game
    {
        /// <summary>Storefront for gold packs; null hides the shop. Set by the host before <see cref="Load"/>.</summary>
        public IStore Store;

        WalletData _wallet = new();
        bool _inShop;
        int _shopSel;               // keyboard selection: 0..3 packs, 4 = BACK
        bool _shopBusy;             // a purchase sheet is up; wait for its outcome
        string _shopMsg;
        Vector4 _shopMsgCol;

        const int ShopBack = -1;
        const int HpCap = 99999;

        void OpenShop()
        {
            if (Store == null) return;
            _inShop = true;
            _shopSel = 0;
            _shopMsg = null;
            if (Store.State == StoreState.Unavailable) Store.Refresh();
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        void CloseShop()
        {
            _inShop = false;
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        /// <summary>Gold the hero can spend, counting purchases that haven't been handed over yet.</summary>
        int Spendable() => _gold + _wallet.Gold;

        // Hero line for the title menu and shop: the saved hero's HP and gold, counting any
        // purchases still waiting in the wallet (they land the moment the run continues).
        // Null when there's no hero and nothing waiting.
        string MenuGoldLine()
        {
            if (_menuSave != null)
            {
                string who = string.IsNullOrEmpty(_menuSave.Name) ? "HERO" : _menuSave.Name;
                int max = WithWalletHp(_menuSave.MaxHp);
                int hp = _wallet.HpDoubles > 0 ? max : _menuSave.Hp; // a doubling also heals to full
                return $"{who}   HP {hp}/{max}   GOLD {_menuSave.Gold + _wallet.Gold}";
            }
            if (_wallet.IsEmpty) return null;
            return _wallet.HpDoubles > 0 ? $"FOR YOUR NEXT HERO: {_wallet.Gold} GOLD + DOUBLE HP"
                                         : $"GOLD FOR YOUR NEXT HERO  {_wallet.Gold}";
        }

        // Max HP once the wallet's pending doublings are applied.
        int WithWalletHp(int maxHp)
        {
            for (int i = 0; i < _wallet.HpDoubles; i++) maxHp = Math.Min(HpCap, maxHp * 2);
            return maxHp;
        }

        bool HeroInPlay => !_inMenu && !_inCreate && !_dead && _level != null;

        // Called every frame: collect purchase outcomes, then hand the wallet to a living hero.
        void PumpStore()
        {
            if (Store != null)
                while (Store.TryTakeEvent(out var e))
                {
                    switch (e.Kind)
                    {
                        case StoreEventKind.Purchased: Deliver(e); break;
                        case StoreEventKind.Cancelled: ShopSay("PURCHASE CANCELLED", new Vector4(0.7f, 0.72f, 0.8f, 1f)); break;
                        case StoreEventKind.Deferred: ShopSay("WAITING FOR APPROVAL", new Vector4(0.7f, 0.85f, 1f, 1f)); break;
                        default: ShopSay(e.Message ?? "PURCHASE FAILED", new Vector4(1f, 0.5f, 0.45f, 1f)); break;
                    }
                    if (e.Kind != StoreEventKind.Purchased) _shopBusy = false;
                }

            if (HeroInPlay && ApplyWallet())
            {
                SaveGame();
                CommitWallet();
            }
        }

        void Deliver(StoreEvent e)
        {
            _shopBusy = false;
            var pack = GoldPacks.Find(e.ProductId);
            if (pack == null || (e.TransactionId != null && _wallet.Credited.Contains(e.TransactionId)))
            {
                Store.Finish(e.Token); // unknown product or already paid out: just close it
                return;
            }

            _wallet.Gold += pack.Gold;
            if (pack.DoubleHp) _wallet.HpDoubles++;
            if (e.TransactionId != null)
            {
                _wallet.Credited.Add(e.TransactionId);
                if (_wallet.Credited.Count > 50) _wallet.Credited.RemoveAt(0);
            }
            // Finish only once the purchase is on disk; otherwise StoreKit redelivers it next launch.
            if (PersistWallet()) Store.Finish(e.Token);

            _audio.Play(Sound.Gold, 0.9f);
            ShopSay(pack.DoubleHp ? $"+{pack.Gold} GOLD AND DOUBLE HP!" : $"+{pack.Gold} GOLD!", new Vector4(1f, 0.9f, 0.4f, 1f));
        }

        void ShopSay(string text, Vector4 color)
        {
            _shopMsg = text;
            _shopMsgCol = color;
        }

        // Move the wallet's contents onto the current hero (doesn't persist anything).
        bool ApplyWallet()
        {
            if (_wallet.IsEmpty) return false;
            var gold = new Vector4(1f, 0.9f, 0.4f, 1f);
            if (_wallet.Gold > 0)
            {
                _gold += _wallet.Gold;
                Log($"+{_wallet.Gold} GOLD FROM THE STORE", gold);
            }
            if (_wallet.HpDoubles > 0)
            {
                for (int i = 0; i < _wallet.HpDoubles; i++) _maxHp = Math.Min(HpCap, _maxHp * 2);
                _hp = _maxHp;
                Log("YOUR MAX HP DOUBLES!", new Vector4(0.7f, 1f, 0.8f, 1f));
            }
            _audio.Play(Sound.Power, 0.8f);
            return true;
        }

        // The hero has been saved with the wallet's contents: empty it.
        void CommitWallet()
        {
            _wallet.Gold = 0;
            _wallet.HpDoubles = 0;
            PersistWallet();
        }

        bool PersistWallet() => _capture || SaveSystem.SaveWallet(_wallet); // capture never touches real files

        void BuyPack(int i)
        {
            var pack = GoldPacks.All[i];
            if (_shopBusy || Store.State != StoreState.Ready || Store.Price(pack.Id) == null) return;
            _shopBusy = true;
            _shopMsg = null;
            _audio.Play(Sound.Step, 0.3f, 0.2f);
            Store.Buy(pack.Id);
        }

        // ---- input -----------------------------------------------------------------------
        void HandleShopKey(Key k)
        {
            int n = GoldPacks.All.Length + 1;
            switch (k)
            {
                case Key.Up: case Key.W: _shopSel = (_shopSel - 1 + n) % n; break;
                case Key.Down: case Key.S: _shopSel = (_shopSel + 1) % n; break;
                case Key.Enter: case Key.Space:
                    if (_shopSel < GoldPacks.All.Length) BuyPack(_shopSel); else CloseShop();
                    break;
                case Key.Escape: CloseShop(); break;
            }
        }

        void ShopPointerDown(float mx, float my)
        {
            foreach (var r in ShopRows())
                if (Inside(mx, my, r.x, r.y, r.w, r.h))
                {
                    if (r.idx == ShopBack) CloseShop(); else BuyPack(r.idx);
                    return;
                }
        }

        // ---- layout + drawing --------------------------------------------------------------
        // One row per pack, then BACK (idx = ShopBack). Shared by render and hit-test.
        List<(int idx, float x, float y, float w, float h)> ShopRows()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            float bw = Math.Min(760 * ui, W - SafeInsets.X - SafeInsets.Z - 40 * ui), bh = 64 * ui, gap = 14 * ui;
            float x = W / 2f - bw / 2f, y0 = H * 0.28f;
            var rows = new List<(int, float, float, float, float)>();
            for (int i = 0; i < GoldPacks.All.Length; i++)
                rows.Add((i, x, y0 + i * (bh + gap), bw, bh));
            float backW = 260 * ui;
            rows.Add((ShopBack, W / 2f - backW / 2f, y0 + GoldPacks.All.Length * (bh + gap) + 44 * ui, backW, 58 * ui));
            return rows;
        }

        void RenderShop()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            var gold = new Vector4(1f, 0.82f, 0.45f, 1f);
            var dim = new Vector4(0.7f, 0.72f, 0.8f, 1f);
            _hud.Begin(W, H);
            _hud.Quad(0, 0, W, H, new Vector4(0.03f, 0.03f, 0.05f, _level != null ? 0.86f : 1f));

            _hud.TextCentered("BUY GOLD", W / 2f, H * 0.09f, 4.4f * ui, gold);
            // when dead, revival heals to max HP, so that's the number that matters
            string sub = _dead ? $"MAX HP {WithWalletHp(_maxHp)}   GOLD {Spendable()}   REVIVE COSTS {ReviveCost()}"
                       : MenuGoldLine() ?? "GOLD GOES TO YOUR NEXT HERO";
            _hud.TextCentered(sub, W / 2f, H * 0.09f + 56 * ui, 1.7f * ui, dim);

            var rows = ShopRows();
            for (int r = 0; r < rows.Count; r++)
            {
                var (idx, x, y, w, h) = rows[r];
                bool hot = (_ptrHeld && Inside(_ptrX, _ptrY, x, y, w, h)) || (!TouchUI && r == _shopSel);
                if (idx == ShopBack)
                {
                    DrawButton(x, y, w, h, "BACK", hot, ui, 2f * ui);
                    continue;
                }

                var pack = GoldPacks.All[idx];
                string price = Store?.Price(pack.Id);
                bool enabled = price != null && !_shopBusy && Store.State == StoreState.Ready;
                if (enabled)
                    DrawButton(x, y, w, h, "", hot, ui);
                else
                {
                    _hud.Quad(x, y, w, h, new Vector4(0.08f, 0.08f, 0.1f, 0.7f));
                    Border(x, y, w, h, 3 * ui, new Vector4(0.25f, 0.25f, 0.3f, 1f));
                }
                float s = 2.2f * ui, ty = y + h / 2f - 4 * s;
                var labelCol = enabled ? (pack.DoubleHp ? new Vector4(0.7f, 1f, 0.8f, 1f) : new Vector4(1f, 0.9f, 0.4f, 1f))
                                       : new Vector4(0.5f, 0.5f, 0.55f, 1f);
                _hud.Text(pack.Label, x + 24 * ui, ty, s, labelCol);
                string p = price ?? "...";
                _hud.Text(p, x + w - 24 * ui - _hud.TextWidth(p, s), ty, s,
                    enabled ? new Vector4(0.96f, 0.96f, 1f, 1f) : new Vector4(0.5f, 0.5f, 0.55f, 1f));
            }

            // status line between the packs and BACK
            string status = _shopMsg;
            var col = _shopMsgCol;
            if (_shopBusy) { status = "CONTACTING THE APP STORE..."; col = dim; }
            else if (status == null && Store?.State == StoreState.Loading) { status = "LOADING THE STORE..."; col = dim; }
            else if (status == null && Store?.State == StoreState.Unavailable) { status = "STORE UNAVAILABLE - TRY AGAIN LATER"; col = new Vector4(1f, 0.5f, 0.45f, 1f); }
            if (status != null)
            {
                var back = rows[^1];
                _hud.TextCentered(status, W / 2f, back.y - 32 * ui, 1.8f * ui, col);
            }

            _hud.End();
        }
    }
}
