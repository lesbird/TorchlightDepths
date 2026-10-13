using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Foundation;
using GameKit;
using UIKit;

namespace Dungeon
{
    /// <summary>
    /// <see cref="ILeaderboards"/> on Game Center. Signs the player in at launch. If they
    /// aren't signed in yet, the sign-in sheet is held until they tap SIGN IN on the
    /// leaderboards panel. Leaderboards are read with GKLeaderboard.LoadEntries, which
    /// returns the top entries, the player's own entry and the total player count. The
    /// game draws all of that itself; Game Center's own leaderboard screens aren't used.
    /// </summary>
    public sealed class GameCenter : ILeaderboards
    {
        readonly Func<UIViewController> _host;
        readonly Dictionary<Board, LbPage> _pages = new();          // main thread only
        readonly ConcurrentQueue<(Board, long)> _best = new();
        UIViewController _signInSheet;

        public GcState State { get; private set; } = GcState.SigningIn;
        public bool CanSignIn => _signInSheet != null && State != GcState.Ready;
        public string Problem { get; private set; }

        const double SignInTimeout = 20; // seconds without any answer from Game Center

        public GameCenter(Func<UIViewController> host)
        {
            _host = host;
            StartTimeout();
            // Game Center calls this at launch and again whenever the sign-in state changes.
            GKLocalPlayer.Local.AuthenticateHandler = (sheet, error) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
            {
                if (sheet != null)
                {
                    _signInSheet = sheet;
                    Problem = null;
                    State = GcState.SignedOut;
                }
                else if (GKLocalPlayer.Local.Authenticated)
                {
                    _signInSheet = null;
                    Problem = null;
                    State = GcState.Ready;
                    _pages.Clear();
                    foreach (var (b, _, _, _) in Boards.All) Refresh(b); // also restores lifetime totals
                }
                else
                {
                    // cancelled, Game Center turned off in Settings, or the app isn't set up for it
                    if (error != null) Console.WriteLine($"[gamecenter] {error.Code}: {error.LocalizedDescription}");
                    Problem = Describe(error);
                    State = error != null && (GKError)(long)error.Code is GKError.GameUnrecognized or GKError.NotSupported
                        ? GcState.Unavailable : GcState.SignedOut;
                }
            });
        }

        public void SignIn()
        {
            var sheet = _signInSheet;
            if (sheet == null || State == GcState.Ready) return;
            _signInSheet = null; // Game Center hands out a fresh one if it's still needed
            State = GcState.SigningIn;
            StartTimeout();
            _host()?.PresentViewController(sheet, true, null);
        }

        // Game Center sometimes never answers (no network, or a build signed without the
        // Game Center entitlement). Don't leave the panel saying "connecting" forever.
        void StartTimeout() => NSTimer.CreateScheduledTimer(SignInTimeout, false, _ =>
        {
            if (State != GcState.SigningIn) return;
            Problem = "GAME CENTER ISN'T RESPONDING - TRY AGAIN LATER";
            State = GcState.Unavailable;
        });

        static string Describe(NSError error)
        {
            if (error == null) return null;
            return (GKError)(long)error.Code switch
            {
                GKError.Cancelled or GKError.UserDenied => "TURN ON GAME CENTER IN SETTINGS TO SEE THE LEADERBOARDS",
                GKError.NotAuthenticated => "SIGN IN TO GAME CENTER IN SETTINGS TO SEE THE LEADERBOARDS",
                GKError.GameUnrecognized or GKError.NotSupported => "GAME CENTER ISN'T SET UP FOR THIS APP YET",
                GKError.CommunicationsFailure => "CAN'T REACH GAME CENTER - CHECK YOUR CONNECTION",
                _ => "GAME CENTER IS UNAVAILABLE",
            };
        }

        public void Submit(Board b, long value)
        {
            if (State != GcState.Ready) return;
            GKLeaderboard.SubmitScore((nint)value, 0, GKLocalPlayer.Local, new[] { Boards.Id(b) }, error =>
            {
                if (error != null) Console.WriteLine($"[gamecenter] submit {b}: {error.LocalizedDescription}");
            });
        }

        public LbPage Page(Board b) => _pages.TryGetValue(b, out var p) ? p : null;

        public bool TryTakeBest(out Board b, out long value)
        {
            bool ok = _best.TryDequeue(out var t);
            (b, value) = t;
            return ok;
        }

        public void Refresh(Board b)
        {
            if (State != GcState.Ready) return;
            var page = Page(b);
            if (page != null && page.Loading) return;
            // keep showing the old entries while the new ones load
            _pages[b] = new LbPage { Loading = page == null, Failed = false, TotalPlayers = page?.TotalPlayers ?? 0,
                                     MyRank = page?.MyRank ?? 0, MyScore = page?.MyScore ?? 0, Top = page?.Top ?? new() };

            GKLeaderboard.LoadLeaderboards(new[] { Boards.Id(b) }, (boards, error) =>
            {
                if (error != null || boards == null || boards.Length == 0)
                {
                    Console.WriteLine($"[gamecenter] load {b}: {error?.LocalizedDescription ?? "not found"}");
                    Fail(b);
                    return;
                }
                boards[0].LoadEntries(GKLeaderboardPlayerScope.Global, GKLeaderboardTimeScope.AllTime, new NSRange(1, 10),
                    (local, entries, total, error2) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
                    {
                        if (error2 != null)
                        {
                            Console.WriteLine($"[gamecenter] entries {b}: {error2.LocalizedDescription}");
                            Fail(b);
                            return;
                        }
                        var p = new LbPage { TotalPlayers = (int)total };
                        if (local != null)
                        {
                            p.MyRank = (int)local.Rank;
                            p.MyScore = local.Score;
                            _best.Enqueue((b, local.Score));
                        }
                        string me = GKLocalPlayer.Local.GamePlayerId;
                        foreach (var e in entries ?? Array.Empty<GKLeaderboardEntry>())
                            p.Top.Add(new LbEntry
                            {
                                Rank = (int)e.Rank,
                                Name = Game.DrawableName(e.Player?.DisplayName),
                                Score = e.Score,
                                Me = e.Player?.GamePlayerId == me,
                            });
                        _pages[b] = p;
                    }));
            });
        }

        void Fail(Board b) => NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            var old = Page(b);
            // a failed refresh keeps the last good page; only an empty one shows the error
            _pages[b] = old != null && old.Top.Count > 0 ? new LbPage
            {
                TotalPlayers = old.TotalPlayers, MyRank = old.MyRank, MyScore = old.MyScore, Top = old.Top,
            } : new LbPage { Failed = true };
        });
    }
}
