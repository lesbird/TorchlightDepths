using System;

namespace Dungeon
{
    /// <summary>
    /// Procedurally composes a short looping music track for a dungeon level.
    /// Pure CPU synthesis (no OpenAL), so it is safe to run on a background thread.
    /// Each depth is seeded deterministically, gets its own tempo/scale/progression,
    /// and turns darker (Phrygian, lower root) the deeper you go.
    /// </summary>
    public static class MusicGen
    {
        const int SR = 44100;

        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };      // Aeolian
        static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };   // darker

        // chord progressions as scale-degree indices (0=i, 2=III, 3=iv, 4=v, 5=VI, 6=VII)
        static readonly int[][] Progressions =
        {
            new[] { 0, 5, 6, 0 },
            new[] { 0, 3, 4, 0 },
            new[] { 0, 6, 5, 4 },
            new[] { 0, 2, 5, 6 },
            new[] { 0, 4, 5, 3 },
        };

        public static short[] Generate(int depth)
        {
            var rnd = new Random(9000 + depth * 131);

            double bpm = 80 + (depth % 6) * 5;                 // 80..105
            double beat = 60.0 / bpm;
            const int beatsPerBar = 4, bars = 8;
            double barDur = beat * beatsPerBar;
            int len = (int)Math.Round(barDur * bars * SR);
            var buf = new float[len];

            int[] scale = (depth >= 4 && rnd.Next(2) == 0) ? Phrygian : Minor;
            int rootMidi = 45 - Math.Min(depth / 2, 7);         // A2 and lower with depth
            var prog = Progressions[rnd.Next(Progressions.Length)];

            // a melody rhythm pattern (8 eighth-steps per bar) chosen per level
            var hit = new bool[8];
            for (int s = 0; s < 8; s++) hit[s] = rnd.Next(100) < (s % 2 == 0 ? 78 : 42);
            hit[0] = true;

            int melodyDeg = 7;         // random-walk melody position (scale degrees)

            for (int b = 0; b < bars; b++)
            {
                int barStart = (int)(b * barDur * SR);
                int deg = prog[b % prog.Length];

                int chordRoot = rootMidi + ScaleNote(scale, deg);
                int third = rootMidi + ScaleNote(scale, deg + 2);
                int fifth = rootMidi + ScaleNote(scale, deg + 4);

                // sustained pad triad across the bar (soft sine)
                AddNote(buf, barStart, barDur, Freq(chordRoot), 0.09f, Wave.Sine, Style.Pad);
                AddNote(buf, barStart, barDur, Freq(third), 0.075f, Wave.Sine, Style.Pad);
                AddNote(buf, barStart, barDur, Freq(fifth), 0.075f, Wave.Sine, Style.Pad);

                // bass on every beat, an octave below the chord root
                for (int be = 0; be < beatsPerBar; be++)
                {
                    int s = barStart + (int)(be * beat * SR);
                    AddNote(buf, s, beat * 0.92, Freq(chordRoot - 12), 0.24f, Wave.Saw, Style.Bass);
                }

                // melody: eighth-note plucks that random-walk the scale, one octave up
                for (int st = 0; st < 8; st++)
                {
                    if (b == 0 && st < 2) { } // let the first hits land on the downbeat below
                    if (!hit[st]) { melodyDeg += rnd.Next(3) - 1; continue; }
                    melodyDeg += rnd.Next(-2, 3);
                    melodyDeg = Math.Clamp(melodyDeg, 4, 12);
                    int mid = rootMidi + 12 + ScaleNote(scale, melodyDeg);
                    int s = barStart + (int)(st * (beat / 2) * SR);
                    AddNote(buf, s, beat / 2 * 0.9, Freq(mid), 0.13f, Wave.Tri, Style.Pluck);
                }

                // percussion: kick on beats 1 & 3, hats on the offbeats
                for (int be = 0; be < beatsPerBar; be++)
                {
                    int s = barStart + (int)(be * beat * SR);
                    if (be == 0 || be == 2) AddKick(buf, s);
                    AddHat(buf, s + (int)(beat / 2 * SR), rnd);
                }
            }

            return Finish(buf);
        }

        // ---- voices ---------------------------------------------------------

        enum Wave { Sine, Tri, Saw, Square }
        enum Style { Pad, Bass, Pluck }

        static void AddNote(float[] buf, int start, double durSec, double freq, float amp, Wave wave, Style style)
        {
            int len = (int)(durSec * SR);
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if ((uint)idx >= (uint)buf.Length) break;
                double t = i / (double)SR;
                double ph = 2 * Math.PI * freq * t;
                float s = wave switch
                {
                    Wave.Square => Math.Sin(ph) >= 0 ? 1f : -1f,
                    Wave.Tri => (float)(Math.Asin(Math.Sin(ph)) * (2 / Math.PI)),
                    Wave.Saw => (float)(2.0 * (t * freq - Math.Floor(0.5 + t * freq))),
                    _ => (float)Math.Sin(ph),
                };
                float env = Env(i, len, style);
                buf[idx] += amp * env * s;
            }
        }

        static float Env(int i, int len, Style style)
        {
            double t = i / (double)SR, frac = i / (double)len;
            switch (style)
            {
                case Style.Pluck: return (float)(Math.Min(1, t / 0.003) * Math.Pow(1 - frac, 3));
                case Style.Bass: return (float)(Math.Min(1, t / 0.004) * Math.Pow(1 - frac, 1.4));
                default: // Pad: soft attack, gentle release, sustain in the middle
                    double atk = Math.Min(1, t / 0.08);
                    double rel = Math.Min(1, (len - i) / (double)SR / 0.15);
                    return (float)(atk * rel);
            }
        }

        static void AddKick(float[] buf, int start)
        {
            int len = (int)(0.13 * SR);
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if ((uint)idx >= (uint)buf.Length) break;
                double frac = i / (double)len, t = i / (double)SR;
                double freq = 40 + 90 * Math.Pow(1 - frac, 3);   // pitch drop
                float env = (float)Math.Pow(1 - frac, 2);
                buf[idx] += 0.5f * env * (float)Math.Sin(2 * Math.PI * freq * t);
            }
        }

        static void AddHat(float[] buf, int start, Random rnd)
        {
            int len = (int)(0.03 * SR);
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if ((uint)idx >= (uint)buf.Length) break;
                float noise = (float)rnd.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.6f;                       // rough high-pass
                float env = (float)Math.Pow(1 - i / (double)len, 2);
                buf[idx] += (noise - lp) * env * 0.06f;
            }
        }

        // ---- utilities ------------------------------------------------------

        static int ScaleNote(int[] scale, int degree)
        {
            int oct = degree / scale.Length, idx = degree % scale.Length;
            if (idx < 0) { idx += scale.Length; oct--; }
            return oct * 12 + scale[idx];
        }

        static double Freq(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

        static short[] Finish(float[] f)
        {
            var s = new short[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                float v = (float)Math.Tanh(f[i] * 0.9) * 0.9f;   // soft limiter
                s[i] = (short)(Math.Clamp(v, -1f, 1f) * 32767);
            }
            return s;
        }
    }
}
