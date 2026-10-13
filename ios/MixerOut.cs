using System;
using System.Collections.Generic;
using AudioToolbox;
using AVFoundation;
using Foundation;

namespace Dungeon
{
    /// <summary>
    /// iOS audio backend: a tiny software mixer (pitched one-shots + two loop channels)
    /// rendered by an AVAudioSourceNode. Everything the game plays is synthesized PCM,
    /// so there's nothing to decode — the mixer just resamples and sums.
    /// </summary>
    public sealed class MixerOut : IAudioOut
    {
        sealed class Voice { public float[] Data; public double Pos; public float Step, Gain; }
        sealed class Loop { public float[] Data; public int Pos; public float Gain; public bool Playing; }

        const int MaxVoices = 24;
        readonly object _lock = new();
        readonly List<float[]> _clips = new();
        readonly List<Voice> _voices = new();
        readonly Loop[] _loops = { new(), new() };   // indexed by LoopChannel
        AVAudioEngine _engine;
        AVAudioSourceNode _node;

        public bool Init()
        {
            // Ambient: game audio mixes with the user's music and honours the silent switch.
            var session = AVAudioSession.SharedInstance();
            session.SetCategory(AVAudioSession.CategoryAmbient, out NSError _);
            session.SetActive(true, out _);

            var format = new AVAudioFormat(Audio.SR, 1); // mono float32, non-interleaved
            _node = new AVAudioSourceNode(format, Render);
            _engine = new AVAudioEngine();
            _engine.AttachNode(_node);
            _engine.Connect(_node, _engine.MainMixerNode, format);
            _engine.Prepare();
            return _engine.StartAndReturnError(out NSError err) && err == null;
        }

        /// <summary>Stop pulling audio while the app is in the background.</summary>
        public void Pause() => _engine?.Pause();

        public void Resume()
        {
            if (_engine == null || _engine.Running) return;
            AVAudioSession.SharedInstance().SetActive(true, out _);
            _engine.StartAndReturnError(out _);
        }

        static float[] ToFloat(short[] pcm)
        {
            var f = new float[pcm.Length];
            for (int i = 0; i < pcm.Length; i++) f[i] = pcm[i] / 32768f;
            return f;
        }

        public int AddClip(short[] pcm)
        {
            lock (_lock) { _clips.Add(ToFloat(pcm)); return _clips.Count - 1; }
        }

        public void PlayClip(int clip, float gain, float pitch)
        {
            lock (_lock)
            {
                if ((uint)clip >= (uint)_clips.Count) return;
                if (_voices.Count >= MaxVoices) _voices.RemoveAt(0); // steal the oldest
                _voices.Add(new Voice { Data = _clips[clip], Step = pitch, Gain = gain });
            }
        }

        public void SetLoop(LoopChannel ch, short[] pcm, float gain, bool playing)
        {
            var data = pcm == null ? null : ToFloat(pcm); // convert outside the lock
            lock (_lock)
            {
                var l = _loops[(int)ch];
                l.Data = data; l.Pos = 0; l.Gain = gain; l.Playing = playing;
            }
        }

        public void SetLoopPlaying(LoopChannel ch, bool playing)
        {
            lock (_lock) _loops[(int)ch].Playing = playing;
        }

        // Audio-thread callback: mix every active voice and loop into the output buffer.
        unsafe int Render(ref bool isSilence, ref AudioTimeStamp timestamp, uint frameCount, AudioBuffers outputData)
        {
            float* outp = (float*)outputData[0].Data;
            int n = (int)frameCount;
            for (int i = 0; i < n; i++) outp[i] = 0f;

            lock (_lock)
            {
                foreach (var l in _loops)
                {
                    if (!l.Playing || l.Data == null || l.Data.Length == 0) continue;
                    var d = l.Data;
                    int pos = l.Pos;
                    for (int i = 0; i < n; i++)
                    {
                        outp[i] += d[pos] * l.Gain;
                        if (++pos >= d.Length) pos = 0;
                    }
                    l.Pos = pos;
                }

                for (int v = _voices.Count - 1; v >= 0; v--)
                {
                    var vo = _voices[v];
                    var d = vo.Data;
                    double pos = vo.Pos;
                    for (int i = 0; i < n; i++)
                    {
                        int p0 = (int)pos;
                        if (p0 >= d.Length - 1) { pos = d.Length; break; }
                        float t = (float)(pos - p0);
                        outp[i] += (d[p0] + (d[p0 + 1] - d[p0]) * t) * vo.Gain; // linear resample
                        pos += vo.Step;
                    }
                    vo.Pos = pos;
                    if (pos >= d.Length - 1) _voices.RemoveAt(v);
                }
            }

            for (int i = 0; i < n; i++) outp[i] = Math.Clamp(outp[i], -1f, 1f);
            isSilence = false;
            return 0;
        }

        public void Dispose()
        {
            _engine?.Stop();
            _engine?.Dispose();
            _node?.Dispose();
            _engine = null; _node = null;
        }
    }
}
