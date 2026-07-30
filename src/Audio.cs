using System;
using System.Collections.Generic;
using OpenTK.Audio.OpenAL;

namespace Dungeon
{
    public enum Sound { Step, Bump, Swing, Hit, Kill, Hurt, Gold, Potion, Power, Descend, Death }

    /// <summary>
    /// Procedurally-synthesized sound effects played through OpenAL. Every effect
    /// is generated as a PCM waveform in code — no audio files. Fails soft: if no
    /// audio device is available the whole engine becomes a no-op.
    /// </summary>
    public sealed class Audio : IDisposable
    {
        const int SR = 44100;
        bool _ok;
        ALDevice _device;
        ALContext _context;
        readonly Dictionary<Sound, int> _buffers = new();
        int[] _sources;
        int _next;
        int _ambientSource = -1;
        int _musicSource = -1;
        int _musicBuffer = -1;
        bool _musicOn = true;
        readonly Random _rng = new(777);

        public bool MusicOn => _musicOn;

        public Audio()
        {
            try
            {
                _device = ALC.OpenDevice(null);
                if (_device == ALDevice.Null) return;
                _context = ALC.CreateContext(_device, (int[])null);
                if (_context == ALContext.Null) return;
                ALC.MakeContextCurrent(_context);

                _sources = new int[24];
                for (int i = 0; i < _sources.Length; i++) _sources[i] = AL.GenSource();

                Build();
                StartAmbient();
                _ok = true;
            }
            catch
            {
                _ok = false; // audio simply won't play
            }
        }

        void Build()
        {
            _buffers[Sound.Step] = Buf(Step());
            _buffers[Sound.Bump] = Buf(Bump());
            _buffers[Sound.Swing] = Buf(Swing());
            _buffers[Sound.Hit] = Buf(Hit());
            _buffers[Sound.Kill] = Buf(Kill());
            _buffers[Sound.Hurt] = Buf(Hurt());
            _buffers[Sound.Gold] = Buf(Gold());
            _buffers[Sound.Potion] = Buf(Potion());
            _buffers[Sound.Power] = Buf(Power());
            _buffers[Sound.Descend] = Buf(Descend());
            _buffers[Sound.Death] = Buf(Death());
        }

        int Buf(short[] pcm)
        {
            int b = AL.GenBuffer();
            AL.BufferData(b, ALFormat.Mono16, pcm, SR);
            return b;
        }

        public void Play(Sound s, float gain = 1f, float pitchJitter = 0f)
        {
            if (!_ok || !_buffers.TryGetValue(s, out int buf)) return;
            int src = -1;
            for (int i = 0; i < _sources.Length; i++)
            {
                AL.GetSource(_sources[i], ALGetSourcei.SourceState, out int st);
                if ((ALSourceState)st != ALSourceState.Playing) { src = _sources[i]; break; }
            }
            if (src == -1) { src = _sources[_next]; _next = (_next + 1) % _sources.Length; }

            float pitch = 1f + ((float)_rng.NextDouble() * 2f - 1f) * pitchJitter;
            AL.Source(src, ALSourcei.Buffer, buf);
            AL.Source(src, ALSourcef.Gain, gain);
            AL.Source(src, ALSourcef.Pitch, pitch <= 0 ? 1f : pitch);
            AL.SourcePlay(src);
        }

        // ---- procedural music track (looping, swapped per level) ------------

        /// <summary>Install a freshly generated level track and (if enabled) start looping it.</summary>
        public void SetMusic(short[] pcm)
        {
            if (!_ok || pcm == null || pcm.Length == 0) return;
            try
            {
                if (_musicSource == -1) _musicSource = AL.GenSource();
                AL.SourceStop(_musicSource);
                AL.Source(_musicSource, ALSourcei.Buffer, 0);
                if (_musicBuffer != -1) AL.DeleteBuffer(_musicBuffer);

                _musicBuffer = AL.GenBuffer();
                AL.BufferData(_musicBuffer, ALFormat.Mono16, pcm, SR);
                AL.Source(_musicSource, ALSourcei.Buffer, _musicBuffer);
                AL.Source(_musicSource, ALSourceb.Looping, true);
                AL.Source(_musicSource, ALSourcef.Gain, 0.5f);
                if (_musicOn) AL.SourcePlay(_musicSource);
            }
            catch { /* music is non-essential */ }
        }

        /// <summary>Toggle the music track on/off. Returns the new state.</summary>
        public bool ToggleMusic()
        {
            _musicOn = !_musicOn;
            if (_ok && _musicSource != -1)
            {
                try
                {
                    if (_musicOn) AL.SourcePlay(_musicSource);
                    else AL.SourcePause(_musicSource);
                }
                catch { }
            }
            return _musicOn;
        }

        void StartAmbient()
        {
            int b = Buf(Ambient());
            _ambientSource = AL.GenSource();
            AL.Source(_ambientSource, ALSourcei.Buffer, b);
            AL.Source(_ambientSource, ALSourceb.Looping, true);
            AL.Source(_ambientSource, ALSourcef.Gain, 0.35f);
            AL.SourcePlay(_ambientSource);
        }

        // =====================================================================
        // Tiny software synth. All helpers return float samples in [-1, 1];
        // Finish() applies a soft limiter and converts to 16-bit PCM.
        // =====================================================================

        static short[] Finish(float[] f, float gain = 0.9f)
        {
            var s = new short[f.Length];
            for (int i = 0; i < f.Length; i++)
            {
                float v = (float)Math.Tanh(f[i] * gain) * 0.95f; // soft clip
                s[i] = (short)(Math.Clamp(v, -1f, 1f) * 32767);
            }
            return s;
        }

        static int N(double seconds) => (int)(seconds * SR);

        // exponential-ish decay envelope with a short attack
        static float Env(int i, int len, double attack, double decayPow)
        {
            double t = i / (double)len;
            double a = attack <= 0 ? 1 : Math.Min(1, (i / (double)SR) / attack);
            double body = Math.Pow(1 - t, decayPow);
            return (float)(a * body);
        }

        float Rand() => (float)_rng.NextDouble() * 2f - 1f;

        // ---- individual effects --------------------------------------------

        short[] Step()
        {
            int len = N(0.10);
            var f = new float[len];
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float noise = Rand();
                lp += (noise - lp) * 0.15f;                     // low-pass -> soft thud
                double t = i / (double)SR;
                float body = (float)Math.Sin(2 * Math.PI * 95 * t);
                f[i] = (lp * 0.7f + body * 0.5f) * Env(i, len, 0.002, 2.2);
            }
            return Finish(f, 0.8f);
        }

        short[] Bump()
        {
            int len = N(0.16);
            var f = new float[len];
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                float noise = Rand();
                lp += (noise - lp) * 0.08f;
                double t = i / (double)SR;
                float body = (float)Math.Sin(2 * Math.PI * 62 * t);
                f[i] = (lp * 0.5f + body * 0.7f) * Env(i, len, 0.002, 2.6);
            }
            return Finish(f, 0.9f);
        }

        short[] Swing()
        {
            int len = N(0.20);
            var f = new float[len];
            float bp = 0, lp = 0;
            for (int i = 0; i < len; i++)
            {
                float noise = Rand();
                lp += (noise - lp) * 0.5f;
                bp = noise - lp;                                 // rough high-pass -> airy whoosh
                double t = i / (double)len;
                float env = (float)(Math.Sin(Math.PI * t));      // swell in and out
                f[i] = bp * env * 0.9f;
            }
            return Finish(f, 0.7f);
        }

        short[] Hit()
        {
            int len = N(0.14);
            var f = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double freq = 200 - 120 * (i / (double)len);     // pitch drop
                float sq = Math.Sin(2 * Math.PI * freq * t) > 0 ? 1 : -1;
                float click = i < N(0.006) ? Rand() : 0;
                f[i] = (sq * 0.6f + click * 0.8f) * Env(i, len, 0.001, 2.0);
            }
            return Finish(f, 1.1f);
        }

        short[] Kill()
        {
            int len = N(0.35);
            var f = new float[len];
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double freq = 320 - 240 * (i / (double)len);
                float tri = (float)(Math.Asin(Math.Sin(2 * Math.PI * freq * t)) * (2 / Math.PI));
                float noise = Rand(); lp += (noise - lp) * 0.2f;
                f[i] = (tri * 0.6f + lp * 0.4f) * Env(i, len, 0.002, 1.6);
            }
            return Finish(f, 0.9f);
        }

        short[] Hurt()
        {
            int len = N(0.28);
            var f = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                float sq = Math.Sin(2 * Math.PI * 108 * t) > 0 ? 1 : -1;
                float grit = Rand() * 0.3f;
                f[i] = (sq + grit) * Env(i, len, 0.001, 1.8);
            }
            return Finish(f, 1.0f);
        }

        short[] Gold()
        {
            var a = Tone(1200, 0.06, "sine", 0.001, 2);
            var b = Tone(1850, 0.09, "sine", 0.001, 2);
            return Finish(Concat(a, b), 0.8f);
        }

        short[] Potion()
        {
            int len = N(0.28);
            var f = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double freq = 420 + 620 * (i / (double)len);
                float s = (float)Math.Sin(2 * Math.PI * freq * t);
                float shimmer = (float)Math.Sin(2 * Math.PI * freq * 2.01 * t) * 0.3f;
                f[i] = (s + shimmer) * Env(i, len, 0.004, 1.4);
            }
            return Finish(f, 0.7f);
        }

        short[] Power()
        {
            var a = Tone(523, 0.08, "tri", 0.002, 2.2);
            var b = Tone(659, 0.08, "tri", 0.002, 2.2);
            var c = Tone(784, 0.14, "tri", 0.002, 2.0);
            return Finish(Concat(Concat(a, b), c), 0.8f);
        }

        short[] Descend()
        {
            int len = N(0.8);
            var f = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double freq = 240 - 170 * (i / (double)len);
                float s = (float)Math.Sin(2 * Math.PI * freq * t);
                float bell = (float)Math.Sin(2 * Math.PI * 523 * t) * (float)Math.Pow(1 - i / (double)len, 4) * 0.4f;
                f[i] = (s * 0.8f + bell) * Env(i, len, 0.01, 1.3);
            }
            return Finish(f, 0.8f);
        }

        short[] Death()
        {
            // slow descending minor arpeggio
            var a = Tone(440, 0.28, "tri", 0.004, 1.6);
            var b = Tone(349, 0.28, "tri", 0.004, 1.6);
            var c = Tone(261, 0.55, "tri", 0.004, 1.2);
            return Finish(Concat(Concat(a, b), c), 0.85f);
        }

        short[] Ambient()
        {
            // 3 seconds of very low, slowly-modulated filtered noise: a cave drone
            int len = N(3.0);
            var f = new float[len];
            float lp = 0;
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                float noise = Rand();
                lp += (noise - lp) * 0.004f;                     // heavy low-pass -> rumble
                float sub = (float)Math.Sin(2 * Math.PI * 48 * t) * 0.25f;
                float swell = (float)(0.6 + 0.4 * Math.Sin(2 * Math.PI * 0.13 * t));
                f[i] = (lp * 3.5f + sub) * swell;
            }
            // cross-fade the loop seam
            int fade = N(0.3);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                f[i] = f[i] * k + f[len - fade + i] * (1 - k);
            }
            return Finish(f, 0.6f);
        }

        // ---- oscillator + buffer utilities ---------------------------------

        float[] Tone(double freq, double dur, string wave, double attack, double decayPow)
        {
            int len = N(dur);
            var f = new float[len];
            for (int i = 0; i < len; i++)
            {
                double t = i / (double)SR;
                double ph = 2 * Math.PI * freq * t;
                float s = wave switch
                {
                    "square" => Math.Sin(ph) > 0 ? 1f : -1f,
                    "tri" => (float)(Math.Asin(Math.Sin(ph)) * (2 / Math.PI)),
                    _ => (float)Math.Sin(ph),
                };
                f[i] = s * Env(i, len, attack, decayPow);
            }
            return f;
        }

        static float[] Concat(float[] a, float[] b)
        {
            var r = new float[a.Length + b.Length];
            Array.Copy(a, r, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        public void Dispose()
        {
            if (!_ok) return;
            try
            {
                if (_musicSource != -1) AL.SourceStop(_musicSource);
                if (_musicBuffer != -1) AL.DeleteBuffer(_musicBuffer);
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                ALC.CloseDevice(_device);
            }
            catch { }
        }
    }
}
