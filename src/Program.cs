using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace Dungeon
{
    public static class Program
    {
        public static void Main()
        {
            var gws = GameWindowSettings.Default;
            gws.UpdateFrequency = 0.0; // uncapped; we manage timing ourselves

            var nws = new NativeWindowSettings
            {
                ClientSize = new Vector2i(1920, 1080),
                Title = "Torchlight Depths — a Claude dungeon crawler",
                // macOS supports up to OpenGL 4.1 core, forward-compatible only.
                API = ContextAPI.OpenGL,
                APIVersion = new Version(4, 1),
                Profile = ContextProfile.Core,
                Flags = ContextFlags.ForwardCompatible,
                Vsync = VSyncMode.On,
                WindowState = WindowState.Fullscreen,
            };

            using var game = new Game(gws, nws);
            game.Run();
        }
    }
}
