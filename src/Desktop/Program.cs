using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Dungeon
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Screenshot mode: `--capture [outDir]` boots windowed, grabs a few scenes, and exits.
            // Icon mode: `--icon out.bmp` renders one square, HUD-free dungeon frame and exits.
            // Mac App Store: `--appstore [dir]` renders the staged screenshot set at 2880x1800
            // (on a Retina display); `--preview [dir]` records the app preview's frames + audio.
            bool capture = false; string capDir = "screenshots", iconPath = null;
            var mode = CaptureMode.Scenes;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--icon" && i + 1 < args.Length) { iconPath = args[++i]; capture = true; }
                var m = args[i] switch
                {
                    "--capture" => CaptureMode.Scenes, "--appstore" => CaptureMode.AppStore,
                    "--preview" => CaptureMode.Preview, _ => (CaptureMode?)null,
                };
                if (m != null)
                {
                    capture = true; mode = m.Value;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) capDir = args[++i];
                }
            }

            var gws = GameWindowSettings.Default;
            gws.UpdateFrequency = 0.0; // uncapped; we manage timing ourselves

            var nws = new NativeWindowSettings
            {
                ClientSize = iconPath != null ? new Vector2i(1024, 1024)
                           : !capture ? new Vector2i(1920, 1080)
                           : mode == CaptureMode.AppStore ? new Vector2i(1440, 900)  // 16:10, as the Mac App Store wants
                           : mode == CaptureMode.Preview ? new Vector2i(960, 540)    // 1920x1080 at 2x
                           : new Vector2i(1280, 720),
                Title = "Torchlight Depths",
                // macOS supports up to OpenGL 4.1 core, forward-compatible only.
                API = ContextAPI.OpenGL,
                APIVersion = new Version(4, 1),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                Vsync = VSyncMode.On,
                WindowState = capture ? WindowState.Normal : WindowState.Fullscreen,
            };

            using var window = new DesktopWindow(gws, nws, capture, capDir, iconPath, mode);
            window.Run();
        }
    }
}
