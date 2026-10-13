using System;
using System.IO;

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
    }
}
