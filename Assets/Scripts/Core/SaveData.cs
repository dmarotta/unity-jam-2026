using UnityEngine;

namespace Lucky
{
    /// <summary>Persistent progress and settings. Banked gold is the currency for the future unlock tree.</summary>
    public static class SaveData
    {
        public static int Gold { get => PlayerPrefs.GetInt("gold", 0); set => SetInt("gold", value); }
        public static int BestDepth { get => PlayerPrefs.GetInt("best_depth", 0); set => SetInt("best_depth", value); }
        public static int Runs { get => PlayerPrefs.GetInt("runs", 0); set => SetInt("runs", value); }
        public static int Wins { get => PlayerPrefs.GetInt("wins", 0); set => SetInt("wins", value); }

        public static float MusicVolume { get => PlayerPrefs.GetFloat("music_vol", 0.7f); set => SetFloat("music_vol", value); }
        public static float SfxVolume { get => PlayerPrefs.GetFloat("sfx_vol", 1f); set => SetFloat("sfx_vol", value); }
        public static bool ScreenShake { get => PlayerPrefs.GetInt("shake", 1) == 1; set => SetInt("shake", value ? 1 : 0); }
        public static bool Flashes { get => PlayerPrefs.GetInt("flash", 1) == 1; set => SetInt("flash", value ? 1 : 0); }
        public static bool AutoSpin { get => PlayerPrefs.GetInt("auto", 0) == 1; set => SetInt("auto", value ? 1 : 0); }

        static void SetInt(string key, int v) { PlayerPrefs.SetInt(key, v); PlayerPrefs.Save(); }
        static void SetFloat(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }
    }
}
