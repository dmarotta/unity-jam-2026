using UnityEngine;

namespace Lucky
{
    /// <summary>Global time effects: hitstop freezes and slow motion (music pitches down with it).</summary>
    public class Juice : MonoBehaviour
    {
        public static Juice I { get; private set; }

        float hitstopUntil;
        float slowScale = 1f, slowUntil, slowRecover = 0.5f;
        float current = 1f;

        void Awake() => I = this;

        public void Hitstop(float seconds) => hitstopUntil = Mathf.Max(hitstopUntil, Time.unscaledTime + seconds);

        public void SlowMo(float scale, float duration, float recover = 0.6f)
        {
            slowScale = scale;
            slowUntil = Time.unscaledTime + duration;
            slowRecover = recover;
            current = Mathf.Min(current, scale);
        }

        public bool Frozen => Time.unscaledTime < hitstopUntil;
        public bool Paused { get; set; }

        void Update()
        {
            float target = Time.unscaledTime < slowUntil ? slowScale : 1f;
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime / Mathf.Max(0.05f, slowRecover));
            Time.timeScale = Frozen || Paused ? 0f : current;
            if (AudioManager.I != null) AudioManager.I.MusicPitch = Mathf.Lerp(0.82f, 1f, Mathf.InverseLerp(0.25f, 1f, current));
        }

        void OnDisable() => Time.timeScale = 1f;
    }
}
