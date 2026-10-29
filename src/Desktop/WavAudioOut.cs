using System;
using System.Collections.Generic;
using System.IO;

namespace Dungeon
{
    /// <summary>
    /// Offline audio backend for `--preview`: mixes clips and loops in game time
    /// (the host calls <see cref="Advance"/> once per fixed-step frame) and saves a
    /// 16-bit stereo WAV, so the recording's sound lines up with its frames exactly.
    /// </summary>
    public sealed class WavAudioOut : IAudioOut
    {
        sealed class Voice { public short[] Pcm; public double Pos; public float Gain, Pitch; public bool Loop, Playing; }

        readonly List<short[]> _clips = new();
        readonly List<Voice> _voices = new();
        readonly Dictionary<LoopChannel, Voice> _loops = new();
        readonly List<short> _out = new();
        double _due; // fractional samples owed by Advance

        public bool Init() => true;

        public int AddClip(short[] pcm) { _clips.Add(pcm); return _clips.Count - 1; }

        public void PlayClip(int clip, float gain, float pitch) =>
            _voices.Add(new Voice { Pcm = _clips[clip], Gain = gain, Pitch = pitch, Playing = true });

        public void SetLoop(LoopChannel ch, short[] pcm, float gain, bool playing) =>
            _loops[ch] = pcm == null ? null : new Voice { Pcm = pcm, Gain = gain, Pitch = 1f, Loop = true, Playing = playing };

        public void SetLoopPlaying(LoopChannel ch, bool playing)
        {
            if (_loops.TryGetValue(ch, out var v) && v != null) v.Playing = playing;
        }

        /// <summary>Mix the next <paramref name="seconds"/> of audio.</summary>
        public void Advance(double seconds)
        {
            _due += seconds * Audio.SR;
            int n = (int)_due; _due -= n;
            for (int i = 0; i < n; i++)
            {
                float s = 0;
                foreach (var v in _loops.Values) if (v != null && v.Playing) s += Sample(v);
                for (int k = _voices.Count - 1; k >= 0; k--)
                {
                    var v = _voices[k];
                    if (v.Pos >= v.Pcm.Length - 1) { _voices.RemoveAt(k); continue; }
                    s += Sample(v);
                }
                short o = (short)Math.Clamp(s * 32767f, -32767f, 32767f);
                _out.Add(o); _out.Add(o);
            }
        }

        static float Sample(Voice v)
        {
            int i = (int)v.Pos; float t = (float)(v.Pos - i);
            int j = i + 1 < v.Pcm.Length ? i + 1 : (v.Loop ? 0 : i);
            float s = (v.Pcm[i] + (v.Pcm[j] - v.Pcm[i]) * t) / 32768f;
            v.Pos += v.Pitch;
            if (v.Loop && v.Pos >= v.Pcm.Length) v.Pos -= v.Pcm.Length;
            return s * v.Gain;
        }

        public void Save(string path)
        {
            using var bw = new BinaryWriter(new FileStream(path, FileMode.Create));
            int bytes = _out.Count * 2;
            bw.Write("RIFF"u8); bw.Write(36 + bytes); bw.Write("WAVE"u8);
            bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)2);
            bw.Write(Audio.SR); bw.Write(Audio.SR * 4); bw.Write((short)4); bw.Write((short)16);
            bw.Write("data"u8); bw.Write(bytes);
            foreach (var s in _out) bw.Write(s);
        }

        public void Dispose() { }
    }
}
