using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL4;

namespace Dungeon
{
    // `--preview`: records an App Store app preview. The host steps the game at a fixed
    // 30 fps; each rendered frame is appended to a raw RGBA file while a scripted
    // "player" drives the real game through the menu, hero creation, and an autopiloted
    // run (fight what's near, grab loot, take the stairs). Tools/encode-preview.swift
    // turns the frames (plus the host's recorded audio) into an .mp4.
    public sealed partial class Game
    {
        public const int PreviewFps = 30;
        const int PreviewFrames = 29 * PreviewFps; // Apple allows 15-30 s

        bool _preview;
        int _pvFrame, _pvCool, _pvDepthStart, _pvW, _pvH;
        FileStream _pvOut;
        byte[] _pvBuf;

        /// <summary>Record the app preview frames into <paramref name="dir"/>, then quit.</summary>
        public void EnablePreviewCapture(string dir) { EnableCapture(dir); _preview = true; }

        void PreviewTick()
        {
            WritePreviewFrame();
            PreviewScript(_pvFrame++);
            if (_pvFrame < PreviewFrames) return;

            _pvOut.Dispose();
            File.WriteAllText(Path.Combine(_capDir, "frames.txt"), $"{_pvW} {_pvH} {_pvFrame} {PreviewFps}\n");
            Console.WriteLine($"[preview] done: {_pvFrame} frames at {_pvW}x{_pvH}");
            _capture = false; Close();
        }

        void WritePreviewFrame()
        {
            if (_pvOut == null)
            {
                _pvW = FramebufferSize.X; _pvH = FramebufferSize.Y;
                _pvBuf = new byte[_pvW * _pvH * 4];
                _pvOut = new FileStream(Path.Combine(_capDir, "frames.rgba"), FileMode.Create);
            }
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, _pvW, _pvH, PixelFormat.Rgba, PixelType.UnsignedByte, _pvBuf); // bottom-up rows
            _pvOut.Write(_pvBuf);
        }

        void PreviewScript(int f)
        {
            // title menu -> NEW GAME
            if (f == 36) { KeyDown(Key.Enter); return; }
            // hero creation: type a name, nudge a couple of attributes, BEGIN
            if (f >= 50 && f < 70 && (f - 50) % 4 == 0) { TextInput("ELARA"[(f - 50) / 4].ToString()); return; }
            switch (f)
            {
                case 80: KeyDown(Key.Tab); return;     // reroll
                case 92: KeyDown(Key.Down); return;    // -> DEX
                case 98: KeyDown(Key.Down); return;    // -> VIT
                case 106: KeyDown(Key.Down); return;   // -> LUK
                case 112: case 117: KeyDown(Key.Left); return;
                case 124: KeyDown(Key.Up); return;     // -> VIT
                case 130: case 135: KeyDown(Key.Right); return;
                case 150: KeyDown(Key.Enter); _pvDepthStart = f; return;
            }
            if (f > 150) Autopilot(f);
        }

        // ---- the "player": one action whenever the last move/turn has settled ----
        void Autopilot(int f)
        {
            if (_dead || _inMenu || _inCreate || _anim || _pendingDescend) return;
            if (_pvCool-- > 0) return;
            _pvCool = 3;
            if (_levelBanner > 2.2f) { _pvDepthStart = f; return; } // let the depth banner land first

            // fight anything in reach
            for (int d = 0; d < 4; d++)
                if (EnemyAt(_px + DX[d], _py + DY[d]) != null) { Face(d, () => KeyDown(Key.Up)); return; }

            // loot and foes nearby, until it's time to move on; then the stairs
            bool linger = f - _pvDepthStart < 6 * PreviewFps;
            int dir = linger ? PathDir(7, (x, y) => (_seen[x, y] && ItemAt(x, y) != null) || EnemyAt(x, y) != null) : -1;
            if (dir < 0) dir = PathDir(999, (x, y) => _level.At(x, y) == Tile.Exit);
            if (dir < 0) dir = PathDir(999, (x, y) => EnemyAt(x, y) != null); // a foe blocks the way: clear it
            if (dir >= 0) Face(dir, () => KeyDown(Key.Up));
            else Console.WriteLine($"[preview] f{f}: autopilot has nowhere to go at ({_px},{_py})");
        }

        void Face(int d, Action then)
        {
            if (d == _dir) then();
            else KeyDown(d == (_dir + 3) % 4 ? Key.Left : Key.Right);
        }

        // BFS from the player; returns the first step toward the nearest cell matching `goal`
        // within `maxDist`, or -1. Enemies block the way but count as goals.
        int PathDir(int maxDist, Func<int, int, bool> goal)
        {
            var first = new int[_level.W, _level.H];
            var dist = new int[_level.W, _level.H];
            for (int x = 0; x < _level.W; x++) for (int y = 0; y < _level.H; y++) dist[x, y] = -1;
            var q = new Queue<(int x, int y)>();
            dist[_px, _py] = 0; q.Enqueue((_px, _py));
            while (q.Count > 0)
            {
                var (cx, cy) = q.Dequeue();
                if (dist[cx, cy] >= maxDist) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!_level.IsWalkable(nx, ny) || dist[nx, ny] >= 0) continue;
                    dist[nx, ny] = dist[cx, cy] + 1;
                    first[nx, ny] = dist[cx, cy] == 0 ? d : first[cx, cy];
                    if (goal(nx, ny)) return first[nx, ny];
                    if (EnemyAt(nx, ny) == null) q.Enqueue((nx, ny));
                }
            }
            return -1;
        }
    }
}
