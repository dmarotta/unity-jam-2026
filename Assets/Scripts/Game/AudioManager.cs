using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lucky
{
    /// <summary>Pooled SFX with pitch variance and spam guards, crossfaded music with ducking and slow-mo pitch.</summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager I { get; private set; }

        GameConfig config;
        readonly List<AudioSource> pool = new();
        readonly Dictionary<string, float> lastPlayed = new();
        readonly Dictionary<string, AudioSource> loops = new();
        AudioSource musicA, musicB;
        bool aIsCurrent = true;
        float duck = 1f, duckTarget = 1f, duckRelease = 2f, duckHoldUntil;
        public float MusicPitch = 1f;

        public void Init(GameConfig cfg)
        {
            I = this;
            config = cfg;
            for (int i = 0; i < 28; i++) pool.Add(NewSource(false));
            musicA = NewSource(true);
            musicB = NewSource(true);
        }

        AudioSource NewSource(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            return s;
        }

        public AudioSource Play(string name, float volume = 1f, float pitch = 1f, float pitchVariance = 0.04f, float minInterval = 0.03f)
        {
            var clip = config.Clip(name);
            if (clip == null) return null;
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(name, out var last) && now - last < minInterval) return null;
            lastPlayed[name] = now;

            AudioSource src = null;
            foreach (var s in pool)
                if (!s.isPlaying) { src = s; break; }
            if (src == null) src = pool[Random.Range(0, pool.Count)];

            src.clip = clip;
            src.volume = volume * SaveData.SfxVolume;
            src.pitch = pitch * (1f + Random.Range(-pitchVariance, pitchVariance));
            src.Play();
            return src;
        }

        public void Loop(string name, bool on, float volume = 1f)
        {
            if (on)
            {
                if (loops.ContainsKey(name)) return;
                var clip = config.Clip(name);
                if (clip == null) return;
                var s = NewSource(true);
                s.clip = clip;
                s.volume = volume * SaveData.SfxVolume;
                s.Play();
                loops[name] = s;
            }
            else if (loops.TryGetValue(name, out var s))
            {
                loops.Remove(name);
                StartCoroutine(FadeOutAndDestroy(s, 0.4f));
            }
        }

        IEnumerator FadeOutAndDestroy(AudioSource s, float time)
        {
            float v = s.volume;
            yield return Tween.Run(time, t => s.volume = v * (1f - t), Ease.Linear, true);
            Destroy(s);
        }

        public void PlayMusic(string name, float fade = 1.2f)
        {
            var clip = config.Clip(name);
            var current = aIsCurrent ? musicA : musicB;
            if (clip == null || current.clip == clip && current.isPlaying) return;
            var next = aIsCurrent ? musicB : musicA;
            aIsCurrent = !aIsCurrent;
            next.clip = clip;
            next.volume = 0f;
            next.Play();
            if (crossfade != null) StopCoroutine(crossfade);
            crossfade = StartCoroutine(Crossfade(current, next, fade));
        }

        Coroutine crossfade;

        /// <summary>Fade the current track out to silence; the next PlayMusic starts fresh.</summary>
        public void StopMusic(float fade)
        {
            if (crossfade != null) StopCoroutine(crossfade);
            crossfade = StartCoroutine(FadeOut(aIsCurrent ? musicA : musicB, fade));
        }

        IEnumerator FadeOut(AudioSource source, float time)
        {
            float start = source.volume;
            yield return Tween.Run(time, t => source.volume = start * (1f - t), Ease.Linear, true);
            source.Stop();
            crossfade = null;
        }

        IEnumerator Crossfade(AudioSource from, AudioSource to, float time)
        {
            float fromStart = from.volume;
            yield return Tween.Run(time, t =>
            {
                from.volume = fromStart * (1f - t);
                to.volume = t * MusicLevel;
            }, Ease.Linear, true);
            from.Stop();
            crossfade = null;
        }

        float MusicLevel => 0.55f * SaveData.MusicVolume * duck;

        /// <summary>Drop music to <paramref name="level"/> for <paramref name="hold"/> seconds, then release.</summary>
        public void Duck(float level, float hold, float release = 1.5f)
        {
            duckTarget = level;
            duck = Mathf.Min(duck, level);
            duckHoldUntil = Time.unscaledTime + hold;
            duckRelease = release;
        }

        void Update()
        {
            if (Time.unscaledTime > duckHoldUntil) duckTarget = 1f;
            duck = Mathf.MoveTowards(duck, duckTarget, Time.unscaledDeltaTime / Mathf.Max(0.05f, duckRelease));
            var current = aIsCurrent ? musicA : musicB;
            if (crossfade == null && current.isPlaying) current.volume = Mathf.MoveTowards(current.volume, MusicLevel, Time.unscaledDeltaTime * 2f);
            musicA.pitch = musicB.pitch = MusicPitch;
        }
    }
}
