using System;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using GLKit;
using OpenGLES;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using UIKit;

namespace Dungeon
{
    /// <summary>
    /// iOS host: an OpenGL ES 3.0 GLKView driven by a CADisplayLink. Feeds the game
    /// its drawable size, safe-area insets, touches (as pointer events) and, on iPads
    /// with a keyboard attached, hardware key presses.
    /// </summary>
    public sealed class GameViewController : UIViewController
    {
        sealed class GameView : GLKView
        {
            public Action OnDraw;
            public GameView(CGRect frame, EAGLContext ctx) : base(frame, ctx) { }
            public override void Draw(CGRect rect) => OnDraw?.Invoke();
        }

        EAGLContext _ctx;
        GameView _view;
        Game _game;
        MixerOut _audio;
        CADisplayLink _link;
        double _last;
        UITouch _touch; // the one finger we're tracking

        public override void LoadView()
        {
            _ctx = new EAGLContext(EAGLRenderingAPI.OpenGLES3);
            _view = new GameView(UIScreen.MainScreen.Bounds, _ctx)
            {
                DrawableColorFormat = GLKViewDrawableColorFormat.RGBA8888,
                DrawableDepthFormat = GLKViewDrawableDepthFormat.Format24,
                MultipleTouchEnabled = false,
                EnableSetNeedsDisplay = false, // we call Display() from the display link
            };
            _view.OnDraw = DrawFrame;
            View = _view;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            EAGLContext.SetCurrentContext(_ctx);
            Shader.GlslHeader = IosGL.GlslHeader;

            _audio = new MixerOut();
            _game = new Game(_audio, touchUI: true);
            // `simctl launch <device> <bundle-id> --capture`: scripted touch walkthrough with
            // screenshots written to the app's Documents/capture folder.
            // `--appstore`: the staged App Store screenshot set, into Documents/appstore.
            var args = NSProcessInfo.ProcessInfo.Arguments;
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Array.IndexOf(args, "--appstore") >= 0)
                _game.EnableAppStoreCapture(System.IO.Path.Combine(docs, "appstore"));
            else if (Array.IndexOf(args, "--capture") >= 0)
                _game.EnableCapture(System.IO.Path.Combine(docs, "capture"));
            // `--fakestore` / `--fakegc` (and the capture modes) stand in for StoreKit / Game Center,
            // which have no products or leaderboards to show until they're set up in App Store Connect.
            bool capturing = Array.IndexOf(args, "--capture") >= 0 || Array.IndexOf(args, "--appstore") >= 0;
            _game.Store = capturing || Array.IndexOf(args, "--fakestore") >= 0 ? new FakeStore() : new AppStoreKit();
            _game.Leaderboards = capturing || Array.IndexOf(args, "--fakegc") >= 0 ? new FakeLeaderboards() : new GameCenter(() => this);
            UpdateSizes();
            _game.Load();

            _link = CADisplayLink.Create(Tick);
            _link.PreferredFrameRateRange = CAFrameRateRange.Create(30, 60, 60);
            _link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        }

        void Tick()
        {
            double now = _link.Timestamp;
            float dt = _last == 0 ? 1f / 60f : (float)Math.Min(now - _last, 0.1);
            _last = now;
            _game.Update(dt);
            _view.Display(); // binds the drawable and calls DrawFrame
        }

        void DrawFrame()
        {
            UpdateSizes();
            _game.Render();
        }

        void UpdateSizes()
        {
            _game.FramebufferSize = new Vector2i((int)_view.DrawableWidth, (int)_view.DrawableHeight);
            var s = (float)_view.ContentScaleFactor;
            var i = View.SafeAreaInsets;
            _game.SafeInsets = new Vector4((float)i.Left * s, (float)i.Top * s, (float)i.Right * s, (float)i.Bottom * s);
        }

        // ---- lifecycle ----------------------------------------------------------
        public void Suspend()
        {
            if (_game == null) return;
            _link.Paused = true;      // no GL work allowed in the background
            _game.Suspend();          // drop to the menu and save the run
            _audio.Pause();
            EAGLContext.SetCurrentContext(_ctx);
            GL.Finish();
        }

        public void Resume()
        {
            if (_game == null) return;
            _audio.Resume();
            _last = 0;
            _link.Paused = false;
        }

        // ---- screen behaviour ---------------------------------------------------
        public override bool PrefersStatusBarHidden() => true;
        public override bool PrefersHomeIndicatorAutoHidden => true;
        // Swipes near the edges are gameplay; make the system wait for a second swipe.
        public override UIRectEdge PreferredScreenEdgesDeferringSystemGestures => UIRectEdge.All;
        public override UIInterfaceOrientationMask GetSupportedInterfaceOrientations() => UIInterfaceOrientationMask.Landscape;

        // ---- touch → pointer (framebuffer pixels) -------------------------------
        (float x, float y) Fb(UITouch t)
        {
            var p = t.LocationInView(_view);
            var s = (float)_view.ContentScaleFactor;
            return ((float)p.X * s, (float)p.Y * s);
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            if (_touch != null || _game == null) return;
            _touch = (UITouch)touches.AnyObject;
            var (x, y) = Fb(_touch);
            _game.PointerDown(x, y);
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            if (_touch == null || !touches.Contains(_touch)) return;
            var (x, y) = Fb(_touch);
            _game.PointerMove(x, y);
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            if (_touch == null || !touches.Contains(_touch)) return;
            var (x, y) = Fb(_touch);
            _touch = null;
            _game.PointerUp(x, y);
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt) => TouchesEnded(touches, evt);

        // ---- hardware keyboard (iPad Magic Keyboard etc.) -----------------------
        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            bool handled = false;
            foreach (UIPress p in presses)
            {
                var key = p.Key;
                if (key == null || _game == null) continue;
                var k = Map(key.KeyCode);
                if (k != Key.Unknown) { _game.KeyDown(k); handled = true; }
                var chars = key.Characters;
                if (!string.IsNullOrEmpty(chars) && (key.ModifierFlags & UIKeyModifierFlags.Command) == 0)
                {
                    _game.TextInput(chars);
                    handled = true;
                }
            }
            if (!handled) base.PressesBegan(presses, evt);
        }

        static Key Map(UIKeyboardHidUsage u) => u switch
        {
            UIKeyboardHidUsage.KeyboardUpArrow => Key.Up,
            UIKeyboardHidUsage.KeyboardDownArrow => Key.Down,
            UIKeyboardHidUsage.KeyboardLeftArrow => Key.Left,
            UIKeyboardHidUsage.KeyboardRightArrow => Key.Right,
            UIKeyboardHidUsage.KeyboardW => Key.W, UIKeyboardHidUsage.KeyboardA => Key.A,
            UIKeyboardHidUsage.KeyboardS => Key.S, UIKeyboardHidUsage.KeyboardD => Key.D,
            UIKeyboardHidUsage.KeyboardQ => Key.Q, UIKeyboardHidUsage.KeyboardE => Key.E,
            UIKeyboardHidUsage.KeyboardF => Key.F, UIKeyboardHidUsage.KeyboardC => Key.C,
            UIKeyboardHidUsage.KeyboardM => Key.M,
            UIKeyboardHidUsage.KeyboardSpacebar => Key.Space,
            UIKeyboardHidUsage.KeyboardReturnOrEnter => Key.Enter,
            UIKeyboardHidUsage.KeyboardEscape => Key.Escape,
            UIKeyboardHidUsage.KeyboardDeleteOrBackspace => Key.Backspace,
            UIKeyboardHidUsage.KeyboardTab => Key.Tab,
            _ => Key.Unknown,
        };
    }
}
