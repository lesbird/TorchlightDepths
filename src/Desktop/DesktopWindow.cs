using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Dungeon
{
    public enum CaptureMode { Scenes, AppStore, Preview }

    /// <summary>Desktop host (macOS / Windows): an OpenTK window that drives <see cref="Game"/>.</summary>
    public sealed class DesktopWindow : GameWindow
    {
        readonly Game _game;
        readonly WavAudioOut _wav; // `--preview` records sound in game time instead of playing it
        readonly string _capDir;

        public DesktopWindow(GameWindowSettings gws, NativeWindowSettings nws, bool capture, string capDir,
                             string iconPath = null, CaptureMode mode = CaptureMode.Scenes)
            : base(gws, nws)
        {
            bool preview = capture && iconPath == null && mode == CaptureMode.Preview;
            if (preview) _wav = new WavAudioOut();
            _capDir = capDir;
            _game = new Game(preview ? _wav : new OpenALOut(), touchUI: false);
            _game.QuitRequested += Close;
            if (iconPath != null) _game.EnableIconCapture(iconPath);
            else if (preview) _game.EnablePreviewCapture(capDir);
            else if (capture && mode == CaptureMode.AppStore) _game.EnableAppStoreCapture(capDir);
            else if (capture) _game.EnableCapture(capDir);
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            _game.FramebufferSize = FramebufferSize;
            _game.Load();
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);
            // the preview steps at a fixed rate so every recorded frame is exactly 1/30 s
            _game.Update(_wav != null ? 1f / Game.PreviewFps : (float)args.Time);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            base.OnRenderFrame(args);
            _game.FramebufferSize = FramebufferSize;
            _game.Render();
            _wav?.Advance(1.0 / Game.PreviewFps);
            SwapBuffers();
        }

        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);
            // Use the framebuffer size (pixels), not the client size (points) —
            // on Retina/HiDPI displays they differ by the DPI scale factor.
            GL.Viewport(0, 0, FramebufferSize.X, FramebufferSize.Y);
        }

        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Keys.F11)
            {
                WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
                return;
            }
            var k = Map(e.Key);
            if (k != Key.Unknown) _game.KeyDown(k);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            _game.TextInput(e.AsString);
        }

        protected override void OnMouseMove(MouseMoveEventArgs e)
        {
            base.OnMouseMove(e);
            var (x, y) = MouseFb();
            _game.PointerMove(x, y);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left) return;
            var (x, y) = MouseFb();
            _game.PointerDown(x, y);
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButton.Left) return;
            var (x, y) = MouseFb();
            _game.PointerUp(x, y);
        }

        // Mouse position in framebuffer pixels (client points scaled by the HiDPI factor).
        (float x, float y) MouseFb()
        {
            float sx = ClientSize.X > 0 ? FramebufferSize.X / (float)ClientSize.X : 1f;
            float sy = ClientSize.Y > 0 ? FramebufferSize.Y / (float)ClientSize.Y : 1f;
            return (MousePosition.X * sx, MousePosition.Y * sy);
        }

        protected override void OnUnload()
        {
            _wav?.Save(System.IO.Path.Combine(_capDir, "audio.wav"));
            _game.Unload();
            base.OnUnload();
        }

        static Key Map(Keys k) => k switch
        {
            Keys.Up => Key.Up, Keys.Down => Key.Down, Keys.Left => Key.Left, Keys.Right => Key.Right,
            Keys.W => Key.W, Keys.A => Key.A, Keys.S => Key.S, Keys.D => Key.D,
            Keys.Q => Key.Q, Keys.E => Key.E, Keys.F => Key.F, Keys.C => Key.C, Keys.M => Key.M,
            Keys.Space => Key.Space, Keys.Enter => Key.Enter, Keys.KeyPadEnter => Key.Enter,
            Keys.Escape => Key.Escape, Keys.Backspace => Key.Backspace, Keys.Tab => Key.Tab,
            _ => Key.Unknown,
        };
    }
}
