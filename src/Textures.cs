using System;

namespace Dungeon
{
    /// <summary>Small software raster buffer used to paint procedural art.</summary>
    public sealed class Pixmap
    {
        public readonly int W, H;
        public readonly byte[] Px; // RGBA8

        public Pixmap(int w, int h) { W = w; H = h; Px = new byte[w * h * 4]; }

        public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
            int i = (y * W + x) * 4;
            Px[i] = r; Px[i + 1] = g; Px[i + 2] = b; Px[i + 3] = a;
        }

        public void FillCircle(float cx, float cy, float rad, byte r, byte g, byte b, byte a = 255)
        {
            int x0 = (int)(cx - rad) - 1, x1 = (int)(cx + rad) + 1;
            int y0 = (int)(cy - rad) - 1, y1 = (int)(cy + rad) + 1;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= rad * rad) Set(x, y, r, g, b, a);
                }
        }

        public void FillRect(int rx, int ry, int rw, int rh, byte r, byte g, byte b, byte a = 255)
        {
            for (int y = ry; y < ry + rh; y++)
                for (int x = rx; x < rx + rw; x++) Set(x, y, r, g, b, a);
        }

        public GlTexture ToTexture(bool nearest, bool mipmap) => new GlTexture(Px, W, H, nearest, mipmap);
    }

    /// <summary>An albedo texture paired with its tangent-space normal map.</summary>
    public readonly struct Surface
    {
        public readonly GlTexture Albedo, Normal;
        public Surface(GlTexture albedo, GlTexture normal) { Albedo = albedo; Normal = normal; }
    }

    /// <summary>Procedural texture + sprite generators. No external asset files needed.</summary>
    public static class Textures
    {
        static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

        // ---- world surfaces (tiling, mipmapped) -----------------------------

        public static Surface StoneWall(int depth)
        {
            const int S = 64;
            var pm = new Pixmap(S, S);
            var h = new float[S * S];        // height field the normal map is derived from
            var rnd = new Random(1000 + depth);
            // deeper levels get a cooler, mossier tint
            int tintR = depth > 3 ? -8 : 6;
            int tintG = depth > 3 ? 4 : 2;
            int bh = 16, bw = 32, mortar = 2;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int row = y / bh;
                    int offset = (row % 2) * (bw / 2);
                    int bx = (x + offset) % bw;
                    int by = y % bh;
                    bool isMortar = by < mortar || bx < mortar;
                    float n = (float)rnd.NextDouble();
                    if (isMortar)
                    {
                        int g = 34 + (int)(n * 14);
                        pm.Set(x, y, Clamp(g), Clamp(g + 2), Clamp(g + 4));
                        h[y * S + x] = 0.12f + n * 0.05f;                 // recessed grooves
                    }
                    else
                    {
                        // shade darker toward the mortar for a beveled look
                        float edge = Math.Min(Math.Min(bx - mortar, bw - 1 - bx), Math.Min(by - mortar, bh - 1 - by));
                        float bevel = Math.Clamp(edge / 5f, 0.55f, 1f);
                        int baseg = (int)((96 + n * 42) * bevel);
                        pm.Set(x, y, Clamp(baseg + 8 + tintR), Clamp(baseg + tintG), Clamp(baseg - 6));
                        h[y * S + x] = 0.55f + bevel * 0.4f + n * 0.05f;  // raised, beveled brick face
                    }
                }
            return new Surface(pm.ToTexture(false, true), HeightToNormal(h, S, 2.2f));
        }

        public static Surface Floor(int depth)
        {
            const int S = 64;
            var pm = new Pixmap(S, S);
            var h = new float[S * S];
            var rnd = new Random(2000 + depth);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // cobblestone: cells with dark grout
                    int cx = x / 16, cy = y / 16;
                    int jitter = (int)(new Random(cx * 73856093 ^ cy * 19349663 ^ depth).NextDouble() * 30);
                    int fx = x % 16, fy = y % 16;
                    float edge = Math.Min(Math.Min(fx, 15 - fx), Math.Min(fy, 15 - fy));
                    float bevel = Math.Clamp(edge / 4f, 0.4f, 1f);
                    float n = (float)rnd.NextDouble();
                    int g = (int)((60 + jitter + n * 20) * bevel);
                    pm.Set(x, y, Clamp(g + 6), Clamp(g + 3), Clamp(g));
                    h[y * S + x] = bevel * 0.85f + n * 0.1f;              // domed stones, recessed grout
                }
            return new Surface(pm.ToTexture(false, true), HeightToNormal(h, S, 1.8f));
        }

        public static Surface Ceiling(int depth)
        {
            const int S = 64;
            var pm = new Pixmap(S, S);
            var h = new float[S * S];
            var rnd = new Random(3000 + depth);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float n = (float)rnd.NextDouble();
                    int g = 26 + (int)(n * 16);
                    // faint cracks
                    bool crack = ((x * 7 + y * 3) % 29) < 2;
                    if (crack) g -= 10;
                    pm.Set(x, y, Clamp(g), Clamp(g), Clamp(g + 3));
                    h[y * S + x] = (crack ? 0.15f : 0.6f) + n * 0.2f;     // gently rough, cracks recessed
                }
            return new Surface(pm.ToTexture(false, true), HeightToNormal(h, S, 1.2f));
        }

        /// <summary>Derive a tiling tangent-space normal map from a height field (Sobel gradient, toroidal wrap).</summary>
        static GlTexture HeightToNormal(float[] h, int S, float strength)
        {
            var pm = new Pixmap(S, S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int xl = (x - 1 + S) % S, xr = (x + 1) % S;
                    int yl = (y - 1 + S) % S, yr = (y + 1) % S;
                    float H(int px, int py) => h[py * S + px];
                    // Sobel gradients
                    float dx = (H(xr, yl) + 2f * H(xr, y) + H(xr, yr))
                             - (H(xl, yl) + 2f * H(xl, y) + H(xl, yr));
                    float dy = (H(xl, yr) + 2f * H(x, yr) + H(xr, yr))
                             - (H(xl, yl) + 2f * H(x, yl) + H(xr, yl));
                    var nx = -dx * strength;
                    var ny = -dy * strength;
                    float nz = 1f;
                    float inv = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    nx *= inv; ny *= inv; nz *= inv;
                    pm.Set(x, y, Clamp((int)((nx * 0.5f + 0.5f) * 255f)),
                                 Clamp((int)((ny * 0.5f + 0.5f) * 255f)),
                                 Clamp((int)((nz * 0.5f + 0.5f) * 255f)));
                }
            return pm.ToTexture(false, true);
        }

        /// <summary>A 1×1 flat normal (points straight out of the surface) for un-bumped tiles.</summary>
        public static GlTexture FlatNormal() => new GlTexture(new byte[] { 128, 128, 255, 255 }, 1, 1, false, false);

        // A glowing exit-portal floor tile.
        public static GlTexture Exit()
        {
            const int S = 64;
            var pm = new Pixmap(S, S);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x - 32, dy = y - 32;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    float ring = MathF.Sin(d * 0.6f) * 0.5f + 0.5f;
                    int r = Clamp((int)(40 + ring * 120));
                    int g = Clamp((int)(120 + ring * 135));
                    int b = Clamp((int)(190 + ring * 65));
                    if (d > 30) { r = 20; g = 30; b = 40; }
                    pm.Set(x, y, (byte)r, (byte)g, (byte)b);
                }
            return pm.ToTexture(false, true);
        }

        // ---- sprites (32x32 RGBA with transparency) --------------------------

        public static GlTexture Slime()
        {
            var pm = Sprite();
            pm.FillCircle(16, 22, 12, 60, 200, 80);
            pm.FillRect(4, 22, 24, 8, 60, 200, 80);
            pm.FillCircle(16, 22, 12, 60, 200, 80);
            // highlight
            pm.FillCircle(12, 17, 4, 150, 240, 150);
            // eyes
            pm.FillCircle(12, 20, 2.2f, 20, 20, 20);
            pm.FillCircle(20, 20, 2.2f, 20, 20, 20);
            pm.FillCircle(12, 20, 1f, 255, 255, 255);
            pm.FillCircle(20, 20, 1f, 255, 255, 255);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Rat()
        {
            var pm = Sprite();
            pm.FillCircle(16, 22, 9, 110, 100, 96);      // body
            pm.FillCircle(23, 20, 5, 120, 108, 104);     // head
            pm.FillCircle(26, 16, 2.5f, 120, 108, 104);  // ear
            for (int i = 0; i < 10; i++) pm.Set(6 - i / 3, 24 + (i % 2), 90, 82, 80); // tail
            pm.FillCircle(25, 20, 1.2f, 20, 10, 10);     // eye
            pm.FillCircle(28, 20, 1.2f, 240, 120, 120);  // nose
            return pm.ToTexture(true, false);
        }

        public static GlTexture Bat()
        {
            var pm = Sprite();
            pm.FillCircle(16, 16, 5, 60, 50, 70);        // body
            // wings
            for (int x = 2; x < 16; x++) { int h = (16 - x); pm.FillRect(x, 12 - h / 2, 1, 8 + h / 2, 50, 42, 62); }
            for (int x = 16; x < 30; x++) { int h = (x - 16); pm.FillRect(x, 12 - h / 2, 1, 8 + h / 2, 50, 42, 62); }
            pm.FillCircle(14, 15, 1.3f, 240, 60, 60);
            pm.FillCircle(18, 15, 1.3f, 240, 60, 60);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Skeleton()
        {
            var pm = Sprite();
            pm.FillCircle(16, 9, 5, 225, 225, 210);      // skull
            pm.FillCircle(14, 9, 1.4f, 20, 20, 20);
            pm.FillCircle(18, 9, 1.4f, 20, 20, 20);
            pm.FillRect(15, 14, 2, 12, 220, 220, 205);   // spine
            pm.FillRect(9, 16, 14, 2, 220, 220, 205);    // ribs / arms
            pm.FillRect(11, 20, 10, 1, 200, 200, 188);
            pm.FillRect(12, 26, 2, 5, 220, 220, 205);    // legs
            pm.FillRect(18, 26, 2, 5, 220, 220, 205);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Potion()
        {
            var pm = Sprite();
            pm.FillRect(13, 8, 6, 3, 180, 180, 190);     // cork
            pm.FillRect(14, 11, 4, 3, 120, 90, 60);
            pm.FillCircle(16, 22, 7, 220, 40, 60);       // flask
            pm.FillRect(13, 16, 6, 6, 220, 40, 60);
            pm.FillCircle(13, 20, 2, 255, 150, 160);     // shine
            return pm.ToTexture(true, false);
        }

        public static GlTexture Gold()
        {
            var pm = Sprite();
            for (int i = 0; i < 7; i++)
            {
                int cx = 10 + (i * 5) % 16;
                int cy = 24 - (i / 3) * 5;
                pm.FillCircle(cx, cy, 3.2f, 230, 200, 40);
                pm.FillCircle(cx - 1, cy - 1, 1.2f, 255, 245, 160);
            }
            return pm.ToTexture(true, false);
        }

        public static GlTexture Sword()
        {
            var pm = Sprite();
            pm.FillRect(15, 3, 2, 18, 210, 220, 235);    // blade
            pm.FillCircle(16, 4, 1.5f, 240, 250, 255);
            pm.FillRect(11, 21, 10, 2, 150, 110, 60);    // guard
            pm.FillRect(15, 23, 2, 6, 110, 80, 50);      // grip
            pm.FillCircle(16, 29, 1.6f, 230, 200, 60);   // pommel
            return pm.ToTexture(true, false);
        }

        public static GlTexture Heart()
        {
            var pm = Sprite();
            pm.FillCircle(12, 14, 5, 220, 40, 70);
            pm.FillCircle(20, 14, 5, 220, 40, 70);
            for (int y = 0; y < 12; y++)
            {
                int half = 10 - y;
                pm.FillRect(16 - half, 16 + y, half * 2, 1, 220, 40, 70);
            }
            pm.FillCircle(13, 12, 1.6f, 255, 150, 170);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Shield()
        {
            var pm = Sprite();
            // heater-shield silhouette: widest at the top, tapering to a point
            const int top = 4, bot = 30;
            for (int y = top; y < bot; y++)
            {
                float t = (float)(y - top) / (bot - top);   // 0 at top .. 1 at point
                int half = (int)(12f * MathF.Sqrt(1f - t));
                if (half <= 0) continue;
                pm.FillRect(16 - half, y, half * 2, 1, 96, 108, 132);      // steel body
                pm.Set(16 - half, y, 190, 200, 220);                        // rim highlight
                pm.Set(16 + half - 1, y, 60, 68, 88);                       // rim shadow
            }
            pm.FillRect(4, top, 24, 1, 200, 210, 230);                      // bright top edge
            // raised boss + emblem cross
            pm.FillRect(15, 8, 2, 16, 70, 80, 100);
            pm.FillRect(9, 13, 14, 2, 70, 80, 100);
            pm.FillCircle(16, 15, 3.6f, 170, 182, 205);
            pm.FillCircle(14.6f, 13.6f, 1.3f, 225, 235, 250);              // shine
            return pm.ToTexture(true, false);
        }

        public static GlTexture Oil()   // a lantern that blazes brighter
        {
            var pm = Sprite();
            pm.FillRect(14, 4, 4, 2, 150, 150, 160);      // handle
            pm.FillRect(11, 7, 10, 16, 84, 74, 58);       // frame
            pm.FillRect(13, 9, 6, 12, 255, 205, 90);      // glowing glass
            pm.FillCircle(16, 15, 3f, 255, 245, 170);     // flame core
            pm.FillRect(10, 22, 12, 3, 108, 92, 68);      // base
            return pm.ToTexture(true, false);
        }

        public static GlTexture Ring()  // gold band with a gem
        {
            var pm = Sprite();
            pm.FillCircle(16, 19, 7f, 232, 190, 52);
            pm.FillCircle(16, 19, 4f, 0, 0, 0, 0);        // punch the hole (transparent)
            pm.FillCircle(16, 8, 3f, 90, 200, 235);       // gem
            pm.FillCircle(15, 7, 1f, 225, 250, 255);      // shine
            return pm.ToTexture(true, false);
        }

        public static GlTexture Gem()   // faceted crystal
        {
            var pm = Sprite();
            for (int y = 6; y <= 26; y++)
            {
                float t = Math.Abs(y - 16) / 10f;
                int half = (int)(9 * (1 - t));
                if (half <= 0) continue;
                pm.FillRect(16 - half, y, half * 2, 1, 150, 96, 232);
            }
            for (int y = 6; y <= 16; y++) pm.Set(16, y, 210, 180, 255);   // facet ridge
            pm.FillCircle(13, 12, 1.4f, 225, 210, 255);                    // glint
            return pm.ToTexture(true, false);
        }

        public static GlTexture Whetstone()  // sharpening stone
        {
            var pm = Sprite();
            pm.FillRect(6, 15, 20, 7, 120, 120, 132);
            pm.FillRect(6, 15, 20, 2, 158, 158, 172);     // top light
            pm.FillRect(6, 21, 20, 1, 78, 78, 90);        // bottom shadow
            pm.Set(11, 17, 235, 235, 245); pm.Set(18, 18, 235, 235, 245); pm.Set(22, 17, 235, 235, 245); // sparks
            return pm.ToTexture(true, false);
        }

        public static GlTexture Bread()  // a loaf
        {
            var pm = Sprite();
            pm.FillCircle(16, 20, 9f, 168, 116, 58);
            pm.FillRect(7, 19, 18, 6, 168, 116, 58);
            pm.FillCircle(16, 17, 8f, 202, 152, 88);      // lighter crust top
            for (int i = -1; i <= 1; i++) pm.FillRect(12 + i * 4, 12, 1, 6, 150, 100, 48); // slashes
            return pm.ToTexture(true, false);
        }

        public static GlTexture Regen()  // healing herb / leaf
        {
            var pm = Sprite();
            pm.FillCircle(13, 16, 6f, 60, 178, 82);
            pm.FillCircle(19, 16, 6f, 60, 178, 82);
            pm.FillRect(15, 16, 2, 12, 90, 66, 40);       // stem
            for (int y = 10; y < 22; y++) pm.Set(13, y, 40, 130, 55);  // vein
            pm.FillCircle(11, 13, 1.4f, 150, 235, 160);   // sheen
            return pm.ToTexture(true, false);
        }

        public static GlTexture Hourglass()  // freezes enemies
        {
            var pm = Sprite();
            pm.FillRect(9, 5, 14, 2, 182, 152, 112);      // top cap
            pm.FillRect(9, 25, 14, 2, 182, 152, 112);     // bottom cap
            for (int y = 7; y < 16; y++) { int half = (int)(6f * (16 - y) / 9f) + 1; pm.FillRect(16 - half, y, half * 2, 1, 150, 205, 235); }
            for (int y = 16; y < 25; y++) { int half = (int)(6f * (y - 15) / 9f) + 1; pm.FillRect(16 - half, y, half * 2, 1, 150, 205, 235); }
            pm.FillRect(14, 15, 4, 3, 235, 205, 120);     // sand at the pinch
            return pm.ToTexture(true, false);
        }

        public static GlTexture Boots()  // haste
        {
            var pm = Sprite();
            pm.FillRect(12, 7, 7, 14, 122, 82, 50);       // shaft
            pm.FillRect(12, 20, 13, 5, 122, 82, 50);      // foot
            pm.FillRect(12, 24, 14, 2, 60, 44, 32);       // sole
            pm.FillRect(7, 12, 5, 3, 235, 235, 245);      // little wing
            pm.FillRect(6, 15, 4, 2, 235, 235, 245);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Map()   // reveals the floor
        {
            var pm = Sprite();
            pm.FillRect(8, 9, 16, 14, 226, 210, 162);     // parchment
            pm.FillRect(6, 8, 3, 16, 178, 148, 98);       // left roll
            pm.FillRect(23, 8, 3, 16, 178, 148, 98);      // right roll
            pm.FillRect(11, 13, 8, 1, 122, 92, 60);       // route
            pm.FillRect(15, 13, 1, 6, 122, 92, 60);
            pm.Set(18, 19, 200, 40, 40); pm.Set(19, 19, 200, 40, 40); // X marks the spot
            pm.Set(18, 20, 200, 40, 40); pm.Set(19, 20, 200, 40, 40);
            return pm.ToTexture(true, false);
        }

        public static GlTexture Bomb()  // fiery area blast
        {
            var pm = Sprite();
            pm.FillCircle(15, 20, 8f, 42, 42, 48);        // iron shell
            pm.FillCircle(12, 17, 2.4f, 96, 96, 108);     // highlight
            pm.FillRect(14, 9, 3, 4, 92, 70, 42);         // fuse collar
            pm.Set(18, 8, 120, 90, 50); pm.Set(19, 7, 120, 90, 50); // fuse
            pm.FillCircle(20, 6, 2f, 255, 180, 60);       // spark
            pm.FillCircle(20, 6, 1f, 255, 245, 190);
            return pm.ToTexture(true, false);
        }

        static Pixmap Sprite() => new Pixmap(32, 32); // fully transparent to start
    }
}
