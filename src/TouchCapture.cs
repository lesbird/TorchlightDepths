using System;
using System.IO;
using OpenTK.Mathematics;

namespace Dungeon
{
    // `--capture` on a touch build: a scripted walkthrough that drives the game through
    // the same PointerDown/Move/Up entry points a finger uses, saving a screenshot at
    // each stop. Doubles as an end-to-end check of the touch controls.
    public sealed partial class Game
    {
        int _tcStep, _tcWait;

        void Tap(float x, float y) { PointerDown(x, y); PointerUp(x, y); }
        void TapCenter((float x, float y, float w, float h) r) => Tap(r.x + r.w / 2f, r.y + r.h / 2f);

        void SwipeBy(float dx, float dy)
        {
            float x = FramebufferSize.X / 2f, y = FramebufferSize.Y / 2f;
            PointerDown(x, y);
            for (int i = 1; i <= 4; i++) PointerMove(x + dx * i / 4f, y + dy * i / 4f);
            PointerUp(x + dx, y + dy);
        }

        void Shot(string name) => SaveScreenshot(Path.Combine(_capDir, $"{_capShot++}-{name}.bmp"));

        void TouchCaptureTick()
        {
            if (_appStore) { AppStoreTick(); return; }
            if (_tcWait-- > 0) return;
            _tcWait = 12; // frames between steps: lets tweens finish and the frame settle
            float ui = FramebufferSize.Y / 720f;
            float sw = 0.12f * FramebufferSize.Y; // comfortably past the swipe threshold

            switch (_tcStep++)
            {
                case 0: Shot("menu"); break;
                case 1:
                    foreach (var b in MenuButtons())
                        if (b.action == MenuAction.NewGame) TapCenter((b.x, b.y, b.w, b.h));
                    break;
                case 2: Shot("create"); break;
                case 3:
                    TapCenter(NameBox());
                    foreach (char c in "ELARAX")
                        foreach (var k in KeyboardKeys(ui))
                            if (k.code == c) TapCenter((k.x, k.y, k.w, k.h));
                    foreach (var k in KeyboardKeys(ui))
                        if (k.code == KeyDelete) TapCenter((k.x, k.y, k.w, k.h)); // ELARAX -> ELARA
                    break;
                case 4: Shot("keyboard"); Console.WriteLine($"[capture] name='{_name}' kbOpen={_kbOpen}"); break;
                case 5:
                    foreach (var k in KeyboardKeys(ui))
                        if (k.code == KeyDone) TapCenter((k.x, k.y, k.w, k.h));
                    foreach (var b in CreateButtons())
                        if (b.action == CreateAction.Begin) TapCenter((b.x, b.y, b.w, b.h));
                    break;
                case 6: Shot("dungeon"); Console.WriteLine($"[capture] start pos=({_px},{_py}) dir={_dir}"); break;
                case 7: SwipeBy(sw, 0); break;    // right -> turn right
                case 8: Console.WriteLine($"[capture] after swipe right dir={_dir}"); SwipeBy(-sw, 0); break; // left -> back
                case 9: Console.WriteLine($"[capture] after swipe left dir={_dir}"); SwipeBy(0, -sw); break;  // up -> forward
                case 10: Console.WriteLine($"[capture] after swipe up pos=({_px},{_py})"); SwipeBy(0, sw); break; // down -> back
                case 11: Console.WriteLine($"[capture] after swipe down pos=({_px},{_py})"); Shot("swiped"); break;
                case 12:
                    foreach (var b in HudButtons(ui))
                        if (b.menu) TapCenter((b.x, b.y, b.w, b.h));
                    break;
                case 13: Shot("paused"); Console.WriteLine($"[capture] inMenu={_inMenu}"); break;
                case 14:
                    _hp = 0; _dead = true; _gold = 500; _inMenu = false; // stage a death screen
                    break;
                case 15: Shot("dead"); Console.WriteLine("[capture] done"); _capture = false; Close(); break;
            }
        }

        // ---- `--appstore`: staged marketing screenshots at the device's native size ----
        bool _appStore;

        /// <summary>Render the App Store screenshot set (touch UI) into <paramref name="dir"/>, then stop.</summary>
        public void EnableAppStoreCapture(string dir) { EnableCapture(dir); _appStore = true; }

        void StoreLog(params (string text, Vector4 color)[] lines)
        {
            _log.Clear();
            foreach (var (t, c) in lines) Log(t, c);
        }

        void AppStoreTick()
        {
            if (_tcWait-- > 0) return;
            _tcWait = 3; // staged scenes have no tweens; a few frames lets the GPU settle
            var gold = new Vector4(1f, 0.85f, 0.5f, 1f);

            switch (_tcStep++)
            {
                case 0: // exploring, with the swipe hints up
                    SetupCaptureDungeon(2, false);
                    _enemies.Clear(); _items.Clear();   // one subject per shot: sprites in a
                    StageFoe(3, EnemyKind.Rat, "RAT");  // 1-wide corridor would overlap
                    _intro = 5f;
                    StoreLog(("ELARA THE ROGUE ENTERS >", gold), ("SOMETHING SKITTERS AHEAD", gold));
                    break;
                case 1: Shot("explore"); break;

                case 2: // toe-to-toe with a skeleton
                    SetupCaptureDungeon(4, false);
                    _enemies.Clear(); _items.Clear();
                    StageFoe(1, EnemyKind.Skeleton, "SKELETON");
                    _hp = 17; _gold = 240;
                    StoreLog(("YOU DODGE THE SKELETON", new Vector4(0.7f, 0.9f, 1f, 1f)),
                             ("HIT SKELETON FOR 5", new Vector4(1f, 0.9f, 0.6f, 1f)),
                             ("CRITICAL! SKELETON FOR 12", new Vector4(1f, 0.7f, 0.3f, 1f)));
                    break;
                case 3: Shot("combat"); break;

                case 4: // deep down, buffs lit and the map filling in
                    SetupCaptureDungeon(6, true);
                    _enemies.Clear(); _items.Clear();
                    StageFoe(2, EnemyKind.Slime, "SLIME");
                    for (int x = 0; x < _level.W; x++)
                        for (int y = 0; y < _level.H; y++)
                            if (Math.Abs(x - _px) + Math.Abs(y - _py) <= 14) _seen[x, y] = true;
                    StoreLog(("SHIELD RAISED - HALVES 3 HITS", new Vector4(0.7f, 0.85f, 1f, 1f)),
                             ("TORCH OIL - THE LIGHT BLAZES", new Vector4(1f, 0.85f, 0.4f, 1f)),
                             ("THE AIR TURNS COLD...", gold));
                    break;
                case 5: Shot("depths"); break;

                case 6: // character creation over the dimmed dungeon
                    _enemies.Clear(); _items.Clear(); // nothing showing through the panel
                    EnterCreate();
                    _name = "ELARA";
                    _str = 9; _dex = 16; _vit = 11; _luk = 8; _createSel = 1;
                    break;
                case 7: Shot("hero"); break;

                case 8: // title menu, run in progress
                    SetupCaptureDungeon(3, false);
                    _enemies.Clear(); _items.Clear(); // keep the corridor behind the menu clean
                    _menuSave = new SaveData { Depth = 3 };
                    _menuItems.Clear();
                    _menuItems.Add(MenuAction.Continue);
                    _menuItems.Add(MenuAction.NewGame);
                    _menuSel = 0; _inMenu = true;
                    break;
                case 9: Shot("title"); Console.WriteLine("[appstore] done"); _capture = false; break;
            }
        }

        void StageFoe(int ahead, EnemyKind kind, string name)
        {
            int x = _px + DX[_dir] * ahead, y = _py + DY[_dir] * ahead;
            if (_level.IsWalkable(x, y))
                _enemies.Add(new Enemy { X = x, Y = y, Rx = x, Ry = y, Kind = kind, Name = name, Hp = 20, MaxHp = 20, Dmg = 4, MoveEvery = 1 });
        }
    }
}
