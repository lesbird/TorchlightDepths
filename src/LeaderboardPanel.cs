using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace Dungeon
{
    // Leaderboards: lifetime totals (deepest level, kills, gold found, deaths) posted to
    // Game Center, and an in-game panel that shows each board's top ten plus the player's
    // own rank out of everyone on it. Drawn in the game's style; it doesn't use Game Center's UI.
    public sealed partial class Game
    {
        /// <summary>Leaderboard service; null hides the LEADERBOARDS menu item. Set by the host before <see cref="Load"/>.</summary>
        public ILeaderboards Leaderboards;

        LifetimeStats _stats = new();
        readonly long[] _posted = { -1, -1, -1, -1 }; // last value submitted per board this session
        bool _gcReady;
        bool _inBoards;
        int _boardTab;

        const int BoardsTab = 0, BoardsBack = 1, BoardsSignIn = 2;

        // ---- stats -------------------------------------------------------------------
        // Save the totals and post whatever changed. Called at checkpoints (new floor, death,
        // the menu) rather than on every kill.
        void RecordStats()
        {
            if (!_capture) SaveSystem.SaveStats(_stats);
            PostStats();
        }

        void PostStats()
        {
            if (Leaderboards == null || Leaderboards.State != GcState.Ready) return;
            foreach (var (b, _, _, _) in Boards.All)
            {
                long v = _stats.Get(b);
                if (v <= 0 || v == _posted[(int)b]) continue;
                Leaderboards.Submit(b, v);
                _posted[(int)b] = v;
            }
        }

        // Called every frame.
        void PumpLeaderboards()
        {
            if (Leaderboards == null) return;
            bool ready = Leaderboards.State == GcState.Ready;
            if (ready && !_gcReady) // just signed in
            {
                Array.Fill(_posted, -1);
                PostStats();
                if (_inBoards) Leaderboards.Refresh(Boards.All[_boardTab].board);
            }
            _gcReady = ready;

            // Game Center remembers the totals across reinstalls and devices: never fall below them.
            bool raised = false;
            while (Leaderboards.TryTakeBest(out var b, out long v))
                if (v > _stats.Get(b)) { _stats.Set(b, v); _posted[(int)b] = v; raised = true; }
            if (raised && !_capture) SaveSystem.SaveStats(_stats);
        }

        // ---- panel -------------------------------------------------------------------
        void OpenBoards()
        {
            if (Leaderboards == null) return;
            _inBoards = true;
            RecordStats();
            Leaderboards.Refresh(Boards.All[_boardTab].board);
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        void CloseBoards()
        {
            _inBoards = false;
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        void SelectBoard(int tab)
        {
            tab = (tab + Boards.All.Length) % Boards.All.Length;
            if (tab == _boardTab) return;
            _boardTab = tab;
            Leaderboards.Refresh(Boards.All[tab].board);
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        void HandleBoardsKey(Key k)
        {
            switch (k)
            {
                case Key.Left: case Key.A: SelectBoard(_boardTab - 1); break;
                case Key.Right: case Key.D: case Key.Tab: SelectBoard(_boardTab + 1); break;
                case Key.Enter: case Key.Space: if (Leaderboards.CanSignIn) Leaderboards.SignIn(); break;
                case Key.Escape: CloseBoards(); break;
            }
        }

        void BoardsPointerDown(float mx, float my)
        {
            foreach (var b in BoardsButtons())
                if (Inside(mx, my, b.x, b.y, b.w, b.h))
                {
                    switch (b.kind)
                    {
                        case BoardsTab: SelectBoard(b.tab); break;
                        case BoardsSignIn: Leaderboards.SignIn(); break;
                        default: CloseBoards(); break;
                    }
                    return;
                }
        }

        // Tabs, BACK, and (when signed out) SIGN IN. Shared by render and hit-test.
        List<(int kind, int tab, string label, float x, float y, float w, float h)> BoardsButtons()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            var list = new List<(int, int, string, float, float, float, float)>();

            float avail = W - SafeInsets.X - SafeInsets.Z - 40 * ui, gap = 14 * ui;
            float tw = Math.Min(200 * ui, (avail - 3 * gap) / 4f), th = 50 * ui;
            float tx = W / 2f - (4 * tw + 3 * gap) / 2f, ty = H * 0.07f + 58 * ui;
            for (int i = 0; i < Boards.All.Length; i++)
                list.Add((BoardsTab, i, Boards.All[i].tab, tx + i * (tw + gap), ty, tw, th));

            float bw = 260 * ui, bh = 54 * ui;
            list.Add((BoardsBack, 0, "BACK", W / 2f - bw / 2f, H - SafeInsets.W - 24 * ui - bh, bw, bh));

            if (Leaderboards.CanSignIn)
            {
                float sw = 340 * ui;
                list.Add((BoardsSignIn, 0, "SIGN IN", W / 2f - sw / 2f, H * 0.56f, sw, 58 * ui));
            }
            return list;
        }

        void RenderBoards()
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float ui = H / 720f;
            var gold = new Vector4(1f, 0.82f, 0.45f, 1f);
            var dim = new Vector4(0.7f, 0.72f, 0.8f, 1f);
            var white = new Vector4(0.96f, 0.96f, 1f, 1f);
            var (board, _, _, title) = Boards.All[_boardTab];

            _hud.Begin(W, H);
            _hud.Quad(0, 0, W, H, new Vector4(0.03f, 0.03f, 0.05f, _level != null ? 0.86f : 1f));
            _hud.TextCentered("LEADERBOARDS", W / 2f, H * 0.07f, 4.4f * ui, gold);

            var buttons = BoardsButtons();
            foreach (var b in buttons)
            {
                bool pressed = _ptrHeld && Inside(_ptrX, _ptrY, b.x, b.y, b.w, b.h);
                bool hot = b.kind == BoardsTab ? b.tab == _boardTab : pressed;
                DrawButton(b.x, b.y, b.w, b.h, b.label, hot, ui, (b.kind == BoardsTab ? 1.9f : 2f) * ui);
            }

            // your standing on this board
            var tabs = buttons[0];
            float y = tabs.y + tabs.h + 26 * ui;
            var page = Leaderboards.State == GcState.Ready ? Leaderboards.Page(board) : null;
            string rank = page == null || page.Loading ? null
                        : page.MyRank > 0 ? $"YOUR RANK  {page.MyRank} OF {page.TotalPlayers}"
                        : $"NOT RANKED YET   {page.TotalPlayers} PLAYERS";
            if (rank != null) _hud.TextCentered(rank, W / 2f, y, 2.4f * ui, gold);
            string mine = board == Board.Deepest ? $"YOUR DEEPEST  {_stats.Deepest}" : $"YOUR {title}  {_stats.Get(board)}";
            _hud.TextCentered(mine, W / 2f, y + 32 * ui, 1.7f * ui, dim);

            float listTop = y + 72 * ui;
            string status = Leaderboards.State switch
            {
                GcState.SigningIn => "CONNECTING TO GAME CENTER...",
                GcState.Unavailable => Leaderboards.Problem ?? "GAME CENTER IS UNAVAILABLE",
                GcState.SignedOut => Leaderboards.CanSignIn ? "SIGN IN TO GAME CENTER TO SEE THE LEADERBOARDS"
                                   : Leaderboards.Problem ?? "TURN ON GAME CENTER IN SETTINGS TO SEE THE LEADERBOARDS",
                _ => page == null || page.Loading ? "LOADING..."
                   : page.Failed ? "COULDN'T LOAD THE LEADERBOARD"
                   : page.Top.Count == 0 ? "NO SCORES YET - BE THE FIRST" : null,
            };
            if (status != null)
            {
                _hud.TextCentered(status, W / 2f, listTop + 40 * ui, 1.8f * ui, dim);
                _hud.End();
                return;
            }

            // top ten
            float avail = W - SafeInsets.X - SafeInsets.Z - 40 * ui;
            float tw = Math.Min(720 * ui, avail), x0 = W / 2f - tw / 2f, rowH = 29 * ui, s = 1.9f * ui;
            for (int i = 0; i < page.Top.Count; i++)
            {
                var e = page.Top[i];
                float ry = listTop + i * rowH;
                if (e.Me) _hud.Quad(x0 - 10 * ui, ry - 6 * ui, tw + 20 * ui, rowH - 2 * ui, new Vector4(0.48f, 0.30f, 0.11f, 0.85f));
                else if (i % 2 == 0) _hud.Quad(x0 - 10 * ui, ry - 6 * ui, tw + 20 * ui, rowH - 2 * ui, new Vector4(1f, 1f, 1f, 0.04f));
                var c = e.Me ? new Vector4(1f, 0.96f, 0.86f, 1f) : i == 0 ? gold : white;
                _hud.Text($"{e.Rank}.", x0, ry, s, c);
                _hud.Text(e.Name, x0 + 80 * ui, ry, s, c);
                string sc = e.Score.ToString();
                _hud.Text(sc, x0 + tw - _hud.TextWidth(sc, s), ry, s, c);
            }
            _hud.End();
        }

        // Game Center names can hold anything; the pixel font is uppercase ASCII.
        public static string DrawableName(string name, int max = 18)
        {
            var chars = new List<char>();
            foreach (char ch in (name ?? "").ToUpperInvariant())
                if (Font.CanDraw(ch)) chars.Add(ch);
            string s = new string(chars.ToArray()).Trim();
            if (s.Length == 0) s = "PLAYER";
            return s.Length > max ? s[..(max - 1)] + "." : s;
        }
    }
}
