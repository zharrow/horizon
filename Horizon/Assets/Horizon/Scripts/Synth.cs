using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Horizon
{
    // Le WebAudio du prototype : des tons courts (enveloppe et glissando exponentiels) et un drone filtré.
    // Tout est généré dans OnAudioFilterRead, sans aucun fichier audio.
    [RequireComponent(typeof(AudioSource))]
    public sealed class Synth : MonoBehaviour
    {
        public enum Wave { Sine, Triangle, Square, Saw }

        sealed class Voice
        {
            public Wave wave;
            public long start;
            public int len;
            public float f0, f1, vol;
            public double phase;
        }

        sealed class Drone
        {
            public double p1, p2;
            public float freq = 55, freqFrom = 55, freqTo = 55;
            public float cut = 380, cutFrom = 380, cutTo = 380;
            public long rampStart, rampLen = 1;
            public float gain, gainFrom, gainTo, gainLen = 1;
            public long gainStart;
            public bool stopping;
            public float z1, z2, z3, z4; // biquad passe-bas
        }

        readonly ConcurrentQueue<Voice> pending = new ConcurrentQueue<Voice>();
        readonly ConcurrentQueue<System.Action> droneCmds = new ConcurrentQueue<System.Action>();
        readonly List<Voice> voices = new List<Voice>(64);
        Drone drone;
        int sr;

        // Muted : le son tourne mais sort en silence. Paused : tout est figé, drone compris.
        public volatile bool Muted, Paused;
        long clock;
        float lastS = -1;

        void Awake()
        {
            sr = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.playOnAwake = true;
            src.spatialBlend = 0;
            src.loop = true;
            src.Play();
        }

        long Now => Interlocked.Read(ref clock);

        public void Tone(float f, float d, Wave type = Wave.Sine, float v = .08f, float slide = 0, float delay = 0)
        {
            pending.Enqueue(new Voice
            {
                wave = type, start = Now + (long)(delay * sr), len = Mathf.Max(1, (int)(d * sr)),
                f0 = f, f1 = slide > 0 ? Mathf.Max(30, f * slide) : f, vol = v
            });
        }

        // ---------- Drone ----------
        static readonly int[] DroneSteps = { 0, 3, 5, 7, 10, 12 };

        public void DroneStart()
        {
            DroneStop();
            droneCmds.Enqueue(() =>
            {
                drone = new Drone { gainStart = clock, gainFrom = 0, gainTo = .05f, gainLen = 1.2f * sr };
            });
        }

        public void DroneEra(int e)
        {
            float fr = 55 * Mathf.Pow(2, DroneSteps[e] / 12f), cut = 380 + e * 160;
            droneCmds.Enqueue(() =>
            {
                if (drone == null) return;
                drone.freqFrom = drone.freq; drone.freqTo = fr;
                drone.cutFrom = drone.cut; drone.cutTo = cut;
                drone.rampStart = clock; drone.rampLen = (long)(1.2f * sr);
            });
        }

        public void DroneStop()
        {
            droneCmds.Enqueue(() =>
            {
                if (drone == null || drone.stopping) return;
                drone.stopping = true;
                drone.gainFrom = drone.gain; drone.gainTo = 0; drone.gainStart = clock; drone.gainLen = .6f * sr;
            });
        }

        // ---------- Effets du prototype ----------
        static readonly int[] Penta = { 0, 2, 4, 7, 9 };
        static float Semi(float root, int n) => root * Mathf.Pow(2, n / 12f);

        public void Absorb(int combo, int tier, bool crit, bool gold)
        {
            float now = Time.unscaledTime;
            if (tier == 0 && !crit && !gold && now - lastS < .03f) return;
            lastS = now;
            int s = Mathf.Min(combo, 29), n = Penta[s % 5] + 12 * (s / 5);
            float f = Semi(220, n);
            Tone(f, .1f, Wave.Triangle, .06f);
            if (tier >= 1) Tone(90 + tier * 20, .28f, Wave.Sine, .18f, .4f);
            if (crit) Tone(f * 2, .22f, Wave.Square, .035f, 1.5f);
            if (gold) { Tone(f * 1.5f, .35f, Wave.Sine, .06f); Tone(f * 2, .35f, Wave.Sine, .05f, 0, .06f); }
        }

        public void Era(int e)
        {
            Tone(38, 1.4f, Wave.Sine, .3f, 3.5f);
            Tone(76, 1.2f, Wave.Saw, .05f, 2.5f);
            float root = Semi(262, DroneSteps[e]);
            int[] ch = { 0, 4, 7, 12, 16, 19 };
            for (int i = 0; i < ch.Length; i++) Tone(Semi(root, ch[i]), .7f, Wave.Triangle, .06f, 0, .25f + i * .07f);
        }

        public void Buy() { int[] ch = { 0, 4, 7, 12 }; for (int i = 0; i < 4; i++) Tone(Semi(330, ch[i]), .25f, Wave.Triangle, .07f, 0, i * .05f); }
        public void No() => Tone(140, .15f, Wave.Square, .04f, .7f);
        public void Tick(float f) => Tone(f, .05f, Wave.Square, .025f);

        public void Slay()
        {
            Tone(50, 1, Wave.Sine, .3f, 4);
            int[] ch = { 0, 5, 9, 12 };
            for (int i = 0; i < 4; i++) Tone(Semi(392, ch[i]), .35f, Wave.Triangle, .07f, 0, .1f + i * .06f);
        }

        public void Zap() => Tone(1200, .06f, Wave.Saw, .025f, .4f);
        public void Beat() { Tone(62, .2f, Wave.Sine, .3f, .6f); Tone(124, .08f, Wave.Triangle, .05f); }
        public void Big() { int[] ch = { 0, 7, 12, 16 }; for (int i = 0; i < 4; i++) Tone(Semi(262, ch[i]), .5f, Wave.Sine, .07f, 0, i * .07f); }
        public void Nova() { Tone(60, 1.6f, Wave.Saw, .12f, 4); Tone(120, 1.6f, Wave.Sine, .1f, 6); }
        public void StartRun() => Tone(80, .5f, Wave.Sine, .16f, 3);

        // ---------- Thread audio ----------
        static float PolyBlep(double t, double dt)
        {
            if (t < dt) { t /= dt; return (float)(t + t - t * t - 1); }
            if (t > 1 - dt) { t = (t - 1) / dt; return (float)(t * t + t + t + 1); }
            return 0;
        }

        static float Osc(Wave w, double ph, double dt)
        {
            switch (w)
            {
                case Wave.Sine: return Mathf.Sin((float)(ph * 2 * System.Math.PI));
                case Wave.Triangle: return (float)(ph < .5 ? 4 * ph - 1 : 3 - 4 * ph);
                case Wave.Saw: return (float)(2 * ph - 1) - PolyBlep(ph, dt);
                default:
                    float sq = ph < .5 ? 1 : -1;
                    sq += PolyBlep(ph, dt);
                    sq -= PolyBlep((ph + .5) % 1, dt);
                    return sq;
            }
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (Paused) { System.Array.Clear(data, 0, data.Length); return; }
            while (pending.TryDequeue(out var v)) voices.Add(v);
            while (droneCmds.TryDequeue(out var cmd)) cmd();

            int frames = data.Length / channels;
            long t0 = clock;
            for (int n = 0; n < frames; n++)
            {
                long t = t0 + n;
                float sum = 0;
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var v = voices[i];
                    long age = t - v.start;
                    if (age < 0) continue;
                    if (age > v.len + sr * .03f) { voices.RemoveAt(i); continue; }
                    float k = Mathf.Min(1, (float)age / v.len);
                    float f = v.f0 == v.f1 ? v.f0 : v.f0 * Mathf.Pow(v.f1 / v.f0, k);
                    // Enveloppe : montée exponentielle en 6 ms, puis descente exponentielle jusqu'à la fin
                    float atk = .006f * sr, g;
                    if (age < atk) g = .0001f * Mathf.Pow(v.vol / .0001f, age / atk);
                    else if (age < v.len) g = v.vol * Mathf.Pow(.0001f / v.vol, (age - atk) / Mathf.Max(1, v.len - atk));
                    else g = 0;
                    double dt = f / sr;
                    sum += Osc(v.wave, v.phase, dt) * g;
                    v.phase += dt; if (v.phase >= 1) v.phase -= 1;
                }

                if (drone != null)
                {
                    var d = drone;
                    float gk = Mathf.Clamp01((t - d.gainStart) / d.gainLen);
                    d.gain = Mathf.Lerp(d.gainFrom, d.gainTo, gk);
                    float rk = Mathf.Clamp01((float)(t - d.rampStart) / d.rampLen);
                    d.freq = d.freqFrom * Mathf.Pow(d.freqTo / d.freqFrom, rk);
                    d.cut = Mathf.Lerp(d.cutFrom, d.cutTo, rk);
                    double dt1 = d.freq / sr, dt2 = d.freq * 1.5 / sr;
                    float o1 = Osc(Wave.Sine, d.p1, dt1), o2 = Osc(Wave.Triangle, d.p2, dt2);
                    d.p1 = (d.p1 + dt1) % 1; d.p2 = (d.p2 + dt2) % 1;
                    // Passe-bas biquad (Q = 0,707)
                    float w0 = 2 * Mathf.PI * d.cut / sr, cs = Mathf.Cos(w0), al = Mathf.Sin(w0) / (2 * .7071f);
                    float a0 = 1 + al, b0 = (1 - cs) / 2 / a0, b1 = (1 - cs) / a0, a1 = -2 * cs / a0, a2 = (1 - al) / a0;
                    float y = b0 * o2 + b1 * d.z1 + b0 * d.z2 - a1 * d.z3 - a2 * d.z4;
                    d.z2 = d.z1; d.z1 = o2; d.z4 = d.z3; d.z3 = y;
                    sum += (o1 + y) * d.gain;
                    if (d.stopping && gk >= 1) drone = null;
                }

                float s = Muted ? 0 : (float)System.Math.Tanh(sum * .7f);
                for (int ch = 0; ch < channels; ch++) data[n * channels + ch] = s;
            }
            Interlocked.Exchange(ref clock, t0 + frames);
        }
    }
}
