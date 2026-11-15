using System.Collections.Generic;
using UnityEngine;

namespace PetStar.Client
{
    /// <summary>
    /// All sound effects are synthesized at startup (original, no third-party or copyrighted audio).
    /// They can later be replaced by licensed recordings with the same Sfx ids.
    /// </summary>
    public static class Audio
    {
        public enum Sfx { Click, PackTear, CardFlip, RareReveal, Evolution, BasicAttack, Skill, Ultimate, Victory, Defeat, Coin }

        private const int Rate = 44100;
        private static readonly Dictionary<Sfx, AudioClip> Clips = new();
        private static AudioSource? _src;
        public static float Volume { get; set; } = 0.8f;

        public static void Init(GameObject host)
        {
            _src = host.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            Volume = PlayerPrefs.GetFloat("settings.sfx", 0.8f);
            Clips[Sfx.Click] = Make("click", 0.06f, t => Sine(1200 - 4000 * t, t) * Env(t, 0.005f, 0.06f) * 0.5f);
            Clips[Sfx.PackTear] = MakeNoise("tear", 0.45f, (t, n) => n * Env(t, 0.02f, 0.45f) * (0.6f + 0.4f * Mathf.Sin(t * 180f)));
            Clips[Sfx.CardFlip] = MakeNoise("flip", 0.18f, (t, n) => n * Env(t, 0.01f, 0.18f) * 0.5f + Sine(500 + 900 * t, t) * Env(t, 0.0f, 0.12f) * 0.15f);
            Clips[Sfx.RareReveal] = Make("rare", 1.2f, t => Chime(t, new[] { 880f, 1108.7f, 1318.5f, 1760f }, 0.09f) * 0.6f);
            Clips[Sfx.Evolution] = Make("evolve", 1.6f, t => (Sine(220 * Mathf.Pow(4, t / 1.6f), t) * 0.4f + Chime(t - 1.1f, new[] { 1046.5f, 1318.5f, 1568f }, 0.06f) * 0.5f) * Env(t, 0.05f, 1.6f));
            Clips[Sfx.BasicAttack] = MakeNoise("attack", 0.2f, (t, n) => (n * 0.5f + Sine(140 - 200 * t, t)) * Env(t, 0.002f, 0.2f) * 0.7f);
            Clips[Sfx.Skill] = Make("skill", 0.5f, t => Chime(t, new[] { 660f, 990f, 1320f }, 0.05f) * 0.5f);
            Clips[Sfx.Ultimate] = MakeNoise("ultimate", 1.0f, (t, n) => (Sine(110, t) + Sine(165, t) + Sine(220, t) * 0.7f + n * 0.4f * (1 - t)) * Env(t, 0.03f, 1.0f) * 0.35f);
            Clips[Sfx.Victory] = Make("victory", 1.4f, t => Chime(t, new[] { 523.3f, 659.3f, 784f, 1046.5f }, 0.14f) * 0.6f);
            Clips[Sfx.Defeat] = Make("defeat", 1.2f, t => Chime(t, new[] { 392f, 349.2f, 311.1f, 261.6f }, 0.2f) * 0.5f);
            Clips[Sfx.Coin] = Make("coin", 0.3f, t => Chime(t, new[] { 1568f, 2093f }, 0.06f) * 0.5f);
        }

        public static void Play(Sfx s, float pitch = 1f)
        {
            if (_src == null || !Clips.TryGetValue(s, out var c)) return;
            _src.pitch = pitch;
            _src.PlayOneShot(c, Volume);
        }

        public static void Haptic()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (PlayerPrefs.GetInt("settings.haptics", 1) == 1) Handheld.Vibrate();
#endif
        }

        private static float Sine(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t);
        private static float Env(float t, float attack, float length) =>
            t < 0 ? 0 : t < attack ? t / Mathf.Max(attack, 1e-4f) : Mathf.Clamp01(1 - (t - attack) / Mathf.Max(length - attack, 1e-4f));

        private static float Chime(float t, float[] notes, float step)
        {
            float v = 0;
            for (int i = 0; i < notes.Length; i++)
            {
                float lt = t - i * step;
                if (lt < 0) continue;
                v += (Sine(notes[i], lt) + 0.3f * Sine(notes[i] * 2, lt)) * Mathf.Exp(-lt * 4f);
            }
            return v / notes.Length * 1.6f;
        }

        private static AudioClip Make(string name, float seconds, System.Func<float, float> fn)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(fn(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeNoise(string name, float seconds, System.Func<float, float, float> fn)
        {
            var rnd = new System.Random(name.GetHashCode());
            float last = 0;
            return Make(name, seconds, t =>
            {
                float white = (float)(rnd.NextDouble() * 2 - 1);
                last = last * 0.7f + white * 0.3f; // soft low-pass
                return fn(t, last);
            });
        }
    }
}
