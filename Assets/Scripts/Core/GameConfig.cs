using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Lucky
{
    /// <summary>Asset registry filled by the editor builder; looked up by asset name at runtime.</summary>
    [CreateAssetMenu(menuName = "Lucky/Game Config")]
    public class GameConfig : ScriptableObject
    {
        public List<Sprite> sprites = new();
        public List<AudioClip> clips = new();
        public Material spriteFX, additive, vertexColor, spriteDefault, spriteLit, videoLit;
        public GameObject lightPrefab;
        public TMP_FontAsset numberFont, titleFont;
        public FontAsset uiNumberFont, uiTitleFont;

        Dictionary<string, Sprite> spriteLookup;
        Dictionary<string, AudioClip> clipLookup;

        public Sprite Sprite(string name)
        {
            spriteLookup ??= BuildLookup(sprites);
            if (spriteLookup.TryGetValue(name, out var s)) return s;
            Debug.LogWarning($"Missing sprite '{name}'");
            spriteLookup[name] = null;
            return null;
        }

        public AudioClip Clip(string name)
        {
            clipLookup ??= BuildLookup(clips);
            return clipLookup.TryGetValue(name, out var c) ? c : null;
        }

        static Dictionary<string, T> BuildLookup<T>(List<T> items) where T : Object
        {
            var d = new Dictionary<string, T>();
            foreach (var item in items)
                if (item != null) d[item.name] = item;
            return d;
        }
    }
}
