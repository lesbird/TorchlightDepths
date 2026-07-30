using System;
using System.Collections.Generic;

namespace Dungeon
{
    public enum Tile : byte { Wall = 0, Floor = 1, Exit = 2 }

    /// <summary>A single dungeon level: random rooms joined by corridors.</summary>
    public sealed class Level
    {
        public readonly int W, H;
        readonly Tile[,] _t;
        public readonly List<(int x, int y)> Rooms = new();
        public (int x, int y) Start;
        public (int x, int y) ExitPos;

        /// <summary>Reconstruct a level from a saved flat tile array.</summary>
        public Level(int w, int h, byte[] flatTiles, int sx, int sy, int ex, int ey)
        {
            W = w; H = h;
            _t = new Tile[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    _t[x, y] = (Tile)flatTiles[y * w + x];
            Start = (sx, sy);
            ExitPos = (ex, ey);
        }

        public byte[] FlatTiles()
        {
            var f = new byte[W * H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    f[y * W + x] = (byte)_t[x, y];
            return f;
        }

        public Level(int w, int h, int depth, Random rnd)
        {
            W = w; H = h;
            _t = new Tile[w, h];
            // start solid
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    _t[x, y] = Tile.Wall;

            int targetRooms = Math.Min(6 + depth, 12);
            int attempts = 60;
            var rects = new List<(int x, int y, int w, int h)>();
            while (attempts-- > 0 && rects.Count < targetRooms)
            {
                int rw = rnd.Next(4, 9), rh = rnd.Next(4, 8);
                int rx = rnd.Next(1, w - rw - 1), ry = rnd.Next(1, h - rh - 1);
                bool overlap = false;
                foreach (var r in rects)
                    if (rx - 1 < r.x + r.w && rx + rw + 1 > r.x && ry - 1 < r.y + r.h && ry + rh + 1 > r.y)
                    { overlap = true; break; }
                if (overlap) continue;
                rects.Add((rx, ry, rw, rh));
                for (int x = rx; x < rx + rw; x++)
                    for (int y = ry; y < ry + rh; y++)
                        _t[x, y] = Tile.Floor;
                Rooms.Add((rx + rw / 2, ry + rh / 2));
            }

            // connect rooms in creation order with L-shaped corridors
            for (int i = 1; i < Rooms.Count; i++)
            {
                var a = Rooms[i - 1];
                var b = Rooms[i];
                if (rnd.Next(2) == 0)
                {
                    CarveH(a.x, b.x, a.y);
                    CarveV(a.y, b.y, b.x);
                }
                else
                {
                    CarveV(a.y, b.y, a.x);
                    CarveH(a.x, b.x, b.y);
                }
            }
            // a couple of extra loops so it isn't a pure tree
            for (int k = 0; k < depth / 2 + 1 && Rooms.Count > 2; k++)
            {
                var a = Rooms[rnd.Next(Rooms.Count)];
                var b = Rooms[rnd.Next(Rooms.Count)];
                CarveH(a.x, b.x, a.y);
                CarveV(a.y, b.y, b.x);
            }

            Start = Rooms.Count > 0 ? Rooms[0] : (w / 2, h / 2);

            // exit = room whose center is farthest (grid-walk-ish) from start
            var best = Start; double bestD = -1;
            foreach (var r in Rooms)
            {
                double d = Math.Abs(r.x - Start.x) + Math.Abs(r.y - Start.y);
                if (d > bestD) { bestD = d; best = r; }
            }
            ExitPos = best;
            _t[best.x, best.y] = Tile.Exit;
        }

        void CarveH(int x0, int x1, int y)
        {
            if (x0 > x1) (x0, x1) = (x1, x0);
            for (int x = x0; x <= x1; x++) if (_t[x, y] == Tile.Wall) _t[x, y] = Tile.Floor;
        }
        void CarveV(int y0, int y1, int x)
        {
            if (y0 > y1) (y0, y1) = (y1, y0);
            for (int y = y0; y <= y1; y++) if (_t[x, y] == Tile.Wall) _t[x, y] = Tile.Floor;
        }

        public Tile At(int x, int y)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return Tile.Wall;
            return _t[x, y];
        }

        public bool IsWall(int x, int y) => At(x, y) == Tile.Wall;
        public bool IsWalkable(int x, int y) => At(x, y) != Tile.Wall;

        /// <summary>All floor (non-exit) cells, useful for spawning.</summary>
        public List<(int x, int y)> FloorCells()
        {
            var list = new List<(int, int)>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (_t[x, y] == Tile.Floor) list.Add((x, y));
            return list;
        }
    }
}
