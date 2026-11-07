using UnityEngine;

namespace Lucky
{
    /// <summary>Persistent progress and settings. Banked gold buys shop items, whose levels persist here.</summary>
    public static class SaveData
    {
        public static int Gold { get => PlayerPrefs.GetInt("gold", 0); set => SetInt("gold", value); }
        public static int BestDepth { get => PlayerPrefs.GetInt("best_depth", 0); set => SetInt("best_depth", value); }
        public static int Runs { get => PlayerPrefs.GetInt("runs", 0); set => SetInt("runs", value); }
        public static int Wins { get => PlayerPrefs.GetInt("wins", 0); set => SetInt("wins", value); }

        /// <summary>Upgrade level of a shop item, or a consumable's per-run stock.</summary>
        public static int Level(string item) => PlayerPrefs.GetInt("item_" + item, 0);
        public static void SetLevel(string item, int level) => SetInt("item_" + item, level);

        /// <summary>Whether the shop has already celebrated a seal breaking.</summary>
        public static bool SealCelebrated(string gate) => PlayerPrefs.GetInt("seal_" + gate, 0) == 1;
        public static void MarkSealCelebrated(string gate) => SetInt("seal_" + gate, 1);

        /// <summary>Wipes gold, records and every shop level; settings are kept.</summary>
        public static void ResetProgress()
        {
            foreach (var key in new[] { "gold", "best_depth", "runs", "wins" }) PlayerPrefs.DeleteKey(key);
            foreach (var item in Balance.Shop)
            {
                PlayerPrefs.DeleteKey("item_" + item.Id);
                PlayerPrefs.DeleteKey("seal_" + item.Id);
            }
            PlayerPrefs.Save();
        }

        public static float MusicVolume { get => PlayerPrefs.GetFloat("music_vol", 0.7f); set => SetFloat("music_vol", value); }
        public static float SfxVolume { get => PlayerPrefs.GetFloat("sfx_vol", 1f); set => SetFloat("sfx_vol", value); }
        public static bool ScreenShake { get => PlayerPrefs.GetInt("shake", 1) == 1; set => SetInt("shake", value ? 1 : 0); }
        public static bool Flashes { get => PlayerPrefs.GetInt("flash", 1) == 1; set => SetInt("flash", value ? 1 : 0); }
        public static bool AutoSpin { get => PlayerPrefs.GetInt("auto", 0) == 1; set => SetInt("auto", value ? 1 : 0); }

        static void SetInt(string key, int v) { PlayerPrefs.SetInt(key, v); PlayerPrefs.Save(); }
        static void SetFloat(string key, float v) { PlayerPrefs.SetFloat(key, v); PlayerPrefs.Save(); }
    }
}
