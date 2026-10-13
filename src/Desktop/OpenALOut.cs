using System;
using System.Collections.Generic;
using OpenTK.Audio.OpenAL;

namespace Dungeon
{
    /// <summary>Desktop audio backend: OpenAL (system OpenAL on macOS, bundled OpenAL-Soft on Windows).</summary>
    public sealed class OpenALOut : IAudioOut
    {
        ALDevice _device;
        ALContext _context;
        bool _ok;
        readonly List<int> _buffers = new();
        int[] _sources;
        int _next;
        readonly Dictionary<LoopChannel, (int src, int buf)> _loops = new();

        public bool Init()
        {
            _device = ALC.OpenDevice(null);
            if (_device == ALDevice.Null) return false;
            _context = ALC.CreateContext(_device, (int[])null);
            if (_context == ALContext.Null) return false;
            ALC.MakeContextCurrent(_context);

            _sources = new int[24];
            for (int i = 0; i < _sources.Length; i++) _sources[i] = AL.GenSource();
            _ok = true;
            return true;
        }

        public int AddClip(short[] pcm)
        {
            int b = AL.GenBuffer();
            AL.BufferData(b, ALFormat.Mono16, pcm, Audio.SR);
            _buffers.Add(b);
            return b;
        }

        public void PlayClip(int clip, float gain, float pitch)
        {
            int src = -1;
            for (int i = 0; i < _sources.Length; i++)
            {
                AL.GetSource(_sources[i], ALGetSourcei.SourceState, out int st);
                if ((ALSourceState)st != ALSourceState.Playing) { src = _sources[i]; break; }
            }
            if (src == -1) { src = _sources[_next]; _next = (_next + 1) % _sources.Length; }

            AL.Source(src, ALSourcei.Buffer, clip);
            AL.Source(src, ALSourcef.Gain, gain);
            AL.Source(src, ALSourcef.Pitch, pitch);
            AL.SourcePlay(src);
        }

        public void SetLoop(LoopChannel ch, short[] pcm, float gain, bool playing)
        {
            if (!_loops.TryGetValue(ch, out var l)) l = (AL.GenSource(), -1);
            AL.SourceStop(l.src);
            AL.Source(l.src, ALSourcei.Buffer, 0);
            if (l.buf != -1) AL.DeleteBuffer(l.buf);
            l.buf = -1;
            if (pcm != null)
            {
                l.buf = AL.GenBuffer();
                AL.BufferData(l.buf, ALFormat.Mono16, pcm, Audio.SR);
                AL.Source(l.src, ALSourcei.Buffer, l.buf);
                AL.Source(l.src, ALSourceb.Looping, true);
                AL.Source(l.src, ALSourcef.Gain, gain);
                if (playing) AL.SourcePlay(l.src);
            }
            _loops[ch] = l;
        }

        public void SetLoopPlaying(LoopChannel ch, bool playing)
        {
            if (!_loops.TryGetValue(ch, out var l) || l.buf == -1) return;
            if (playing) AL.SourcePlay(l.src);
            else AL.SourcePause(l.src);
        }

        public void Dispose()
        {
            if (!_ok) return;
            try
            {
                foreach (var l in _loops.Values)
                {
                    AL.SourceStop(l.src);
                    if (l.buf != -1) AL.DeleteBuffer(l.buf);
                }
                ALC.MakeContextCurrent(ALContext.Null);
                ALC.DestroyContext(_context);
                ALC.CloseDevice(_device);
            }
            catch { }
            _ok = false;
        }
    }
}
