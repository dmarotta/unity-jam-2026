using System.Collections.Generic;
using UnityEngine;

namespace Lucky
{
    /// <summary>Spritesheet animation extracted from generated video (Resources/Anim/name.png + name.json).</summary>
    public class Flipbook
    {
        const float PixelsPerUnit = 100f;

        [System.Serializable]
        class Meta
        {
            public int frames, columns, frameWidth, frameHeight, hitFrame;
            public float fps, pivotX, pivotY, bodyHeight;
            public bool loop;
        }

        public Sprite[] Frames;
        public float Fps;
        public bool Loop;
        public int HitFrame;
        public float BodyHeightUnits;

        public float Duration => Frames.Length / Fps;
        public float HitTime => Mathf.Max(0, HitFrame) / Fps;

        static readonly Dictionary<string, Flipbook> Cache = new();

        public static Flipbook Load(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            var tex = Resources.Load<Texture2D>("Anim/" + name);
            var json = Resources.Load<TextAsset>("Anim/" + name);
            var normal = Resources.Load<Texture2D>("Anim/" + name + "_n");
            var secondary = normal != null
                ? new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } }
                : new SecondarySpriteTexture[0];
            Flipbook book = null;
            if (tex != null && json != null)
            {
                var m = JsonUtility.FromJson<Meta>(json.text);
                book = new Flipbook
                {
                    Frames = new Sprite[m.frames],
                    Fps = m.fps,
                    Loop = m.loop,
                    HitFrame = m.hitFrame,
                    BodyHeightUnits = m.bodyHeight / PixelsPerUnit,
                };
                for (int i = 0; i < m.frames; i++)
                {
                    int col = i % m.columns, row = i / m.columns;
                    var rect = new Rect(col * m.frameWidth, tex.height - (row + 1) * m.frameHeight, m.frameWidth, m.frameHeight);
                    book.Frames[i] = Sprite.Create(tex, rect, new Vector2(m.pivotX, m.pivotY), PixelsPerUnit, 0, SpriteMeshType.FullRect, Vector4.zero, false, secondary);
                    book.Frames[i].name = $"{name}_{i}";
                }
            }
            Cache[name] = book;
            return book;
        }
    }
}
