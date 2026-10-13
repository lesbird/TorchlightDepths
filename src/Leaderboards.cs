using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Dungeon
{
    public enum Board { Deepest, Kills, Gold, Deaths }

    /// <summary>The four leaderboards. Ids must match the leaderboards in App Store Connect (Game Center).</summary>
    public static class Boards
    {
        public static readonly (Board board, string id, string tab, string title)[] All =
        {
            (Board.Deepest, "com.lesbird.torchlightdepths.deepest", "DEEPEST", "DEEPEST LEVEL"),
            (Board.Kills, "com.lesbird.torchlightdepths.kills", "KILLS", "TOTAL KILLS"),
            (Board.Gold, "com.lesbird.torchlightdepths.gold", "GOLD", "TOTAL GOLD"),
            (Board.Deaths, "com.lesbird.torchlightdepths.deaths", "DEATHS", "TOTAL DEATHS"),
        };

        public static string Id(Board b) => All[(int)b].id;
    }

    public enum GcState { SignedOut, SigningIn, Ready, Unavailable }

    public sealed class LbEntry
    {
        public int Rank;
        public string Name;     // already limited to characters the HUD font can draw
        public long Score;
        public bool Me;
    }

    /// <summary>One leaderboard as last loaded: the top entries plus where the local player stands.</summary>
    public sealed class LbPage
    {
        public bool Loading, Failed;
        public int TotalPlayers;
        public int MyRank;          // 0 = no score posted yet
        public long MyScore;
        public List<LbEntry> Top = new();
    }

    /// <summary>Platform leaderboard service (Game Center on iOS). Polled from the game thread.</summary>
    public interface ILeaderboards
    {
        GcState State { get; }
        /// <summary>A sign-in sheet is available (the player isn't signed in to Game Center yet).</summary>
        bool CanSignIn { get; }
        /// <summary>Why Game Center isn't usable (shown on the panel when not signed in), or null.</summary>
        string Problem { get; }
        void SignIn();
        void Submit(Board b, long value);
        /// <summary>Reload a board's top entries and the local player's rank.</summary>
        void Refresh(Board b);
        LbPage Page(Board b);
        /// <summary>The local player's posted scores, so lifetime totals survive a reinstall or a new device.</summary>
        bool TryTakeBest(out Board b, out long value);
    }

    /// <summary>Lifetime totals across every hero, posted to the leaderboards. Kept in stats.json.</summary>
    public sealed class LifetimeStats
    {
        public long Deepest { get; set; }
        public long Kills { get; set; }
        public long Gold { get; set; }      // found in the dungeon; purchased gold doesn't count
        public long Deaths { get; set; }

        public long Get(Board b) => b switch
        {
            Board.Deepest => Deepest, Board.Kills => Kills, Board.Gold => Gold, _ => Deaths,
        };

        public void Set(Board b, long v)
        {
            switch (b)
            {
                case Board.Deepest: Deepest = v; break;
                case Board.Kills: Kills = v; break;
                case Board.Gold: Gold = v; break;
                default: Deaths = v; break;
            }
        }
    }

    /// <summary>
    /// Stand-in for the simulator and capture runs: a signed-in player on boards full of
    /// made-up rivals, with the player's own submitted totals slotted in.
    /// </summary>
    public sealed class FakeLeaderboards : ILeaderboards
    {
        static readonly string[] Rivals =
        {
            "GRIMWALD", "SERA NIGHTSHADE", "OLD TOM", "BRAMBLE", "KESTREL", "MORDECAI",
            "IVY THE BOLD", "DUSKRUNNER", "HALVARD", "PIP", "WREN", "CINDERS",
        };

        readonly Dictionary<Board, long> _mine = new();
        readonly ConcurrentQueue<(Board, long)> _best = new();

        public GcState State => GcState.Ready;
        public bool CanSignIn => false;
        public string Problem => null;
        public void SignIn() { }
        public void Submit(Board b, long value) => _mine[b] = Math.Max(value, _mine.GetValueOrDefault(b));
        public void Refresh(Board b) { }
        public bool TryTakeBest(out Board b, out long value)
        {
            bool ok = _best.TryDequeue(out var t);
            (b, value) = t;
            return ok;
        }

        public LbPage Page(Board b)
        {
            // a smooth falloff from a board-specific record
            long top = b switch { Board.Deepest => 41, Board.Kills => 18450, Board.Gold => 96210, _ => 512 };
            long mine = _mine.GetValueOrDefault(b);
            var page = new LbPage { TotalPlayers = 2841 + (int)b * 97 };
            var scores = new List<(string name, long score, bool me)>();
            for (int i = 0; i < 10; i++)
                scores.Add((Rivals[(i + (int)b * 3) % Rivals.Length], top * (12 - i) / 12, false));
            int myRank = 0;
            if (mine > 0)
            {
                int above = scores.FindAll(s => s.score > mine).Count;
                myRank = above < 10 ? above + 1 : 12 + (int)(page.TotalPlayers * (1.0 - (double)mine / top) * 0.6);
                if (above < 10) { scores.Insert(above, ("YOU", mine, true)); scores.RemoveAt(10); }
            }
            for (int i = 0; i < scores.Count; i++)
                page.Top.Add(new LbEntry { Rank = i + 1, Name = scores[i].name, Score = scores[i].score, Me = scores[i].me });
            page.MyRank = myRank;
            page.MyScore = mine;
            return page;
        }
    }
}
