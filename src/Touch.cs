using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace Dungeon
{
    // Touch-first controls (active when TouchUI is set, i.e. on iPhone / iPad):
    //   * swipes drive the hero — up = forward/attack, down = back, left/right = turn
    //   * on-screen MENU / MUSIC buttons replace Esc / M
    //   * an in-theme on-screen keyboard for the hero name (letters, digits, space, delete)
    //   * tappable REVIVE / NEW HERO buttons on the death screen
    public sealed partial class Game
    {
        enum Swipe { None, Up, Down, Left, Right }

        bool _swipeArmed;               // finger went down in the dungeon, gesture not used yet
        float _swipeX0, _swipeY0;       // where it went down
        Swipe _queuedSwipe;             // swipe made mid-step, run when the step finishes
        bool _kbOpen;                   // on-screen name keyboard is showing

        // Distance a finger must travel to count as a swipe (fraction of the short screen side).
        const float SwipeFrac = 0.06f;

        // ---- pointer routing (returns true when the touch layer consumed the press) ----
        bool TouchPointerDown(float mx, float my)
        {
            float ui = FramebufferSize.Y / 720f;

            if (_inMenu) return false; // menu buttons hit-test the same for touch and mouse

            if (_inCreate)
            {
                if (_kbOpen)
                {
                    foreach (var k in KeyboardKeys(ui))
                        if (Inside(mx, my, k.x, k.y, k.w, k.h)) { PressKey(k.code); return true; }
                    // a tap anywhere outside the keys dismisses the keyboard
                    _kbOpen = false;
                    return true;
                }
                var (bx, by, bw, bh) = NameBox();
                if (Inside(mx, my, bx, by, bw, bh))
                {
                    _kbOpen = true;
                    _audio.Play(Sound.Step, 0.3f, 0.2f);
                    return true;
                }
                return false; // -/+ and REROLL / BEGIN / BACK use the shared hit-test
            }

            if (_dead)
            {
                foreach (var b in DeathButtons(ui))
                    if (b.enabled && Inside(mx, my, b.x, b.y, b.w, b.h))
                    {
                        switch (b.kind)
                        {
                            case DeathRevive: Revive(); break;
                            case DeathShop: OpenShop(); break;
                            default: EnterCreate(); break;
                        }
                        return true;
                    }
                return true;
            }

            foreach (var b in HudButtons(ui))
                if (Inside(mx, my, b.x, b.y, b.w, b.h))
                {
                    if (b.menu) EnterMenu(); else ToggleMusic();
                    return true;
                }

            _swipeArmed = true;
            _swipeX0 = mx; _swipeY0 = my;
            return true;
        }

        void TouchPointerMove(float mx, float my)
        {
            if (!_swipeArmed || _inMenu || _inCreate || _dead) return;
            float dx = mx - _swipeX0, dy = my - _swipeY0;
            float min = SwipeFrac * Math.Min(FramebufferSize.X, FramebufferSize.Y);
            if (dx * dx + dy * dy < min * min) return;

            // one action per gesture, fired as soon as the finger has clearly moved
            _swipeArmed = false;
            Swipe s = Math.Abs(dx) > Math.Abs(dy)
                ? (dx < 0 ? Swipe.Left : Swipe.Right)
                : (dy < 0 ? Swipe.Up : Swipe.Down);   // screen y grows downward
            if (_anim || _pendingDescend) _queuedSwipe = s;
            else DoSwipe(s);
        }

        void TouchPointerUp(float mx, float my) => _swipeArmed = false;

        void RunQueuedSwipe()
        {
            var s = _queuedSwipe;
            _queuedSwipe = Swipe.None;
            if (s != Swipe.None && !_inMenu && !_inCreate && !_dead) DoSwipe(s);
        }

        void DoSwipe(Swipe s)
        {
            switch (s)
            {
                case Swipe.Up: Step(_dir, attack: true); break;            // bump-attacks a monster ahead
                case Swipe.Down: Step((_dir + 2) % 4, attack: false); break;
                case Swipe.Left: Turn(-1); break;
                case Swipe.Right: Turn(+1); break;
            }
        }

        // ---- in-dungeon buttons (bottom-right, clear of the home indicator) ---------------
        List<(bool menu, string label, float x, float y, float w, float h)> HudButtons(float ui)
        {
            float bw = 150 * ui, bh = 56 * ui, gap = 16 * ui;
            float right = FramebufferSize.X - SafeInsets.Z - 24 * ui;
            float y = FramebufferSize.Y - SafeInsets.W - 24 * ui - bh;
            return new()
            {
                (false, "MUSIC", right - 2 * bw - gap, y, bw, bh),
                (true, "MENU", right - bw, y, bw, bh),
            };
        }

        void DrawHudButtons(float ui)
        {
            foreach (var b in HudButtons(ui))
                DrawButton(b.x, b.y, b.w, b.h, b.label, _ptrHeld && Inside(_ptrX, _ptrY, b.x, b.y, b.w, b.h), ui, 2f * ui);
        }

        // ---- death screen buttons --------------------------------------------------------
        const int DeathRevive = 0, DeathNewHero = 1, DeathShop = 2;

        List<(int kind, bool enabled, string label, float x, float y, float w, float h)> DeathButtons(float ui)
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            float bw = 330 * ui, bh = 62 * ui, gap = 28 * ui;
            float x0 = W / 2f - bw - gap / 2f, y = H / 2f + 40 * ui;
            int cost = ReviveCost();
            bool can = Spendable() >= cost;
            var list = new List<(int, bool, string, float, float, float, float)>
            {
                (DeathRevive, can, can ? $"REVIVE  {cost} GOLD" : $"REVIVE NEEDS {cost}", x0, y, bw, bh),
                (DeathNewHero, true, "NEW HERO", x0 + bw + gap, y, bw, bh),
            };
            if (Store != null) list.Add((DeathShop, true, "BUY GOLD", W / 2f - bw / 2f, y + bh + 22 * ui, bw, bh));
            return list;
        }

        void DrawDeathButtons(float ui)
        {
            foreach (var b in DeathButtons(ui))
            {
                if (b.enabled)
                    DrawButton(b.x, b.y, b.w, b.h, b.label, _ptrHeld && Inside(_ptrX, _ptrY, b.x, b.y, b.w, b.h), ui, 2f * ui);
                else
                {
                    _hud.Quad(b.x, b.y, b.w, b.h, new Vector4(0.08f, 0.08f, 0.1f, 0.7f));
                    Border(b.x, b.y, b.w, b.h, 3 * ui, new Vector4(0.25f, 0.25f, 0.3f, 1f));
                    _hud.TextCentered(b.label, b.x + b.w / 2f, b.y + b.h / 2f - 7 * ui, 1.8f * ui, new Vector4(0.6f, 0.45f, 0.45f, 1f));
                }
            }
        }

        // ---- name entry ----------------------------------------------------------------
        // Shared by the create screen's render and the tap-to-type hit-test.
        (float x, float y, float w, float h) NameBox()
        {
            float ui = FramebufferSize.Y / 720f;
            float w = 560 * ui, h = 58 * ui;
            return (FramebufferSize.X / 2f - w / 2f, FramebufferSize.Y * 0.2f, w, h);
        }

        const char KeyDelete = '\b', KeyDone = '\n';
        static readonly string[] KbRows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL\b", "ZXCVBNM \n" };

        // Every row spans 10 key-units: SPACE is two units wide so the bottom row lines up.
        List<(char code, string label, float x, float y, float w, float h)> KeyboardKeys(float ui)
        {
            int W = FramebufferSize.X, H = FramebufferSize.Y;
            var box = NameBox();
            float top = box.y + box.h + 28 * ui;
            float bottom = H - SafeInsets.W - 20 * ui;
            float gap = 10 * ui;
            float avail = W - SafeInsets.X - SafeInsets.Z - 40 * ui;
            float panelW = Math.Min(avail, 1000 * ui);
            float unit = (panelW - 9 * gap) / 10f;
            float kh = Math.Min(84 * ui, (bottom - top - 3 * gap) / 4f);
            float x0 = SafeInsets.X + (W - SafeInsets.X - SafeInsets.Z) / 2f - panelW / 2f;

            var keys = new List<(char, string, float, float, float, float)>();
            for (int r = 0; r < KbRows.Length; r++)
            {
                float x = x0, y = top + r * (kh + gap);
                foreach (char c in KbRows[r])
                {
                    float w = c == ' ' ? 2 * unit + gap : unit;
                    string label = c switch { KeyDelete => "DEL", KeyDone => "DONE", ' ' => "SPACE", _ => c.ToString() };
                    keys.Add((c, label, x, y, w, kh));
                    x += w + gap;
                }
            }
            return keys;
        }

        void PressKey(char code)
        {
            switch (code)
            {
                case KeyDone: _kbOpen = false; break;
                case KeyDelete: NameBackspace(); break;
                default: NameAppend(code); break;
            }
            _audio.Play(Sound.Step, 0.3f, 0.2f);
        }

        void DrawTouchKeyboard(int W, int H, float ui)
        {
            var keys = KeyboardKeys(ui);
            var box = NameBox();
            // the keyboard sits over the attribute rows, so shade them out completely
            float top = box.y + box.h + 14 * ui;
            _hud.Quad(0, top, W, H - top, new Vector4(0.03f, 0.03f, 0.05f, 1f));
            foreach (var k in keys)
            {
                bool hot = _ptrHeld && Inside(_ptrX, _ptrY, k.x, k.y, k.w, k.h);
                bool wide = k.label.Length > 1;
                DrawButton(k.x, k.y, k.w, k.h, k.label, hot, ui, (wide ? 1.8f : 2.6f) * ui);
            }
        }
    }
}
