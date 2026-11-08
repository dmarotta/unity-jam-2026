using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Lucky
{
    /// <summary>Pooled sprite particles, pop sprites, homing coins and damage numbers.</summary>
    public class FX : MonoBehaviour
    {
        public static FX I { get; private set; }

        /// <summary>Behind (embers), Sky (god rays, rising souls: above the backdrop, behind the fighters), Front, Text.</summary>
        public const int OrderBehind = 5, OrderSky = 10, OrderFront = 400, OrderText = 500;

        public struct Burst
        {
            public string Sprite;
            public int Count;
            public float SpeedMin, SpeedMax, AngleMin, AngleMax;
            public float Gravity, Drag, LifeMin, LifeMax, SizeMin, SizeMax, EndSize, Spin;
            public Color Color, EndColor;
            public bool Additive, Flip, Unscaled, RandomHue;
            public int Order;
            public Vector2 Spread;
        }

        class Particle
        {
            public SpriteRenderer Sr;
            public bool Additive, Flip, Unscaled;
            public Vector3 Vel;
            public float Life, MaxLife, Gravity, Drag, RotVel, Size0, Size1, FlipPhase;
            public Color C0, C1;
            public Vector3 BaseScale;
        }

        GameConfig config;
        readonly List<Particle> live = new();
        readonly Stack<Particle> freeAlpha = new(), freeAdd = new();
        readonly Stack<TextMeshPro> freeText = new();
        readonly Stack<Light2D> freeLights = new();

        /// <summary>A real 2D light that flares and fades — impacts and spells light up the normal-mapped scene.</summary>
        public void Light(Vector3 pos, Color color, float intensity, float radius, float life)
        {
            var light = freeLights.Count > 0 ? freeLights.Pop() : Instantiate(config.lightPrefab, transform).GetComponent<Light2D>();
            light.gameObject.SetActive(true);
            light.transform.position = pos;
            light.color = color;
            light.pointLightOuterRadius = radius;
            StartCoroutine(FadeLight(light, intensity, life));
        }

        IEnumerator FadeLight(Light2D light, float intensity, float life)
        {
            yield return Tween.Run(life, t => light.intensity = intensity * (1f - t), Ease.OutQuad, true);
            light.gameObject.SetActive(false);
            freeLights.Push(light);
        }

        public void Init(GameConfig cfg)
        {
            I = this;
            config = cfg;
        }

        Particle Take(bool additive)
        {
            var stack = additive ? freeAdd : freeAlpha;
            Particle p;
            if (stack.Count > 0)
            {
                p = stack.Pop();
                p.Sr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject(additive ? "fx+" : "fx");
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sharedMaterial = additive ? config.additive : config.spriteDefault;
                p = new Particle { Sr = sr, Additive = additive };
            }
            // Hidden until Update applies this use's scale and colour; otherwise a spawn after this frame's Update
            // renders once with stale or default state (a recycled huge glow, or a fresh renderer's full-size white).
            p.Sr.color = Color.clear;
            return p;
        }

        void Release(Particle p)
        {
            p.Sr.gameObject.SetActive(false);
            (p.Additive ? freeAdd : freeAlpha).Push(p);
        }

        public void Emit(Vector3 pos, Burst b)
        {
            var sprite = config.Sprite(b.Sprite);
            if (sprite == null) return;
            float spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            for (int i = 0; i < b.Count; i++)
            {
                var p = Take(b.Additive);
                p.Sr.sprite = sprite;
                p.Sr.sortingOrder = b.Order == 0 ? OrderFront : b.Order;
                float ang = UnityEngine.Random.Range(b.AngleMin, b.AngleMax) * Mathf.Deg2Rad;
                float spd = UnityEngine.Random.Range(b.SpeedMin, b.SpeedMax);
                p.Vel = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang)) * spd;
                p.MaxLife = p.Life = UnityEngine.Random.Range(b.LifeMin, b.LifeMax);
                p.Gravity = b.Gravity;
                p.Drag = b.Drag;
                p.RotVel = UnityEngine.Random.Range(-b.Spin, b.Spin);
                float size = UnityEngine.Random.Range(b.SizeMin, b.SizeMax) / spriteSize;
                p.Size0 = size;
                p.Size1 = size * b.EndSize;
                p.C0 = b.RandomHue ? Color.HSVToRGB(UnityEngine.Random.value, 0.7f, 1f) : b.Color;
                p.C1 = b.RandomHue ? p.C0 : b.EndColor;
                p.Flip = b.Flip;
                p.FlipPhase = UnityEngine.Random.value * 10f;
                p.Unscaled = b.Unscaled;
                p.BaseScale = Vector3.one;
                var offset = new Vector3(UnityEngine.Random.Range(-b.Spread.x, b.Spread.x), UnityEngine.Random.Range(-b.Spread.y, b.Spread.y));
                p.Sr.transform.SetPositionAndRotation(pos + offset, Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f)));
                live.Add(p);
            }
        }

        /// <summary>A single sprite that scales from size0 to size1 and fades out.</summary>
        public void Pop(string sprite, Vector3 pos, float size0, float size1, float life, Color color, float rotation = 0f, bool additive = true, int order = 0, bool unscaled = true)
        {
            var s = config.Sprite(sprite);
            if (s == null) return;
            float spriteSize = Mathf.Max(s.bounds.size.x, s.bounds.size.y);
            var p = Take(additive);
            p.Sr.sprite = s;
            p.Sr.sortingOrder = order == 0 ? OrderFront : order;
            p.Vel = Vector3.zero;
            p.MaxLife = p.Life = life;
            p.Gravity = p.Drag = p.RotVel = 0f;
            p.Size0 = size0 / spriteSize;
            p.Size1 = size1 / spriteSize;
            p.C0 = color;
            p.C1 = new Color(color.r, color.g, color.b, 0f);
            p.Flip = false;
            p.Unscaled = unscaled;
            p.BaseScale = Vector3.one;
            p.Sr.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 0, rotation));
            live.Add(p);
        }

        public void Ring(Vector3 pos, Color color, float size = 6f, float life = 0.45f) => Pop("vfx_ring", pos, 0.3f, size, life, color);

        /// <summary>Ghost copy of a sprite renderer that fades out (dash trails).</summary>
        public void AfterImage(SpriteRenderer src, Color tint, float life = 0.28f)
        {
            var p = Take(true);
            p.Sr.sprite = src.sprite;
            p.Sr.sortingOrder = src.sortingOrder - 1;
            p.Vel = Vector3.zero;
            p.MaxLife = p.Life = life;
            p.Gravity = p.Drag = p.RotVel = 0f;
            p.Size0 = p.Size1 = 1f;
            p.BaseScale = src.transform.lossyScale;
            p.C0 = tint;
            p.C1 = new Color(tint.r, tint.g, tint.b, 0f);
            p.Flip = false;
            p.Unscaled = false;
            p.Sr.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            live.Add(p);
        }

        void Update()
        {
            float sdt = Time.deltaTime, udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var p = live[i];
                float dt = p.Unscaled ? udt : sdt;
                p.Life -= dt;
                if (p.Life <= 0f)
                {
                    live.RemoveAt(i);
                    Release(p);
                    continue;
                }
                float t = 1f - p.Life / p.MaxLife;
                p.Vel.y += p.Gravity * dt;
                p.Vel *= Mathf.Max(0f, 1f - p.Drag * dt);
                var tr = p.Sr.transform;
                tr.position += p.Vel * dt;
                tr.Rotate(0f, 0f, p.RotVel * dt);
                float pop = t < 0.12f ? Ease.OutBack(t / 0.12f) : 1f;
                float size = Mathf.Lerp(p.Size0, p.Size1, t) * pop;
                float flip = p.Flip ? Mathf.Abs(Mathf.Cos((t * 9f + p.FlipPhase) * Mathf.PI)) * 0.85f + 0.15f : 1f;
                tr.localScale = new Vector3(p.BaseScale.x * size * flip, p.BaseScale.y * size, 1f);
                var c = Color.Lerp(p.C0, p.C1, t);
                if (t > 0.7f) c.a *= 1f - (t - 0.7f) / 0.3f;
                p.Sr.color = c;
            }
        }

        // ---------- Presets ----------

        public void Coins(Vector3 pos, int count, float power = 1f) => Emit(pos, new Burst
        {
            Sprite = "vfx_coin", Count = count, SpeedMin = 5f * power, SpeedMax = 12f * power, AngleMin = 45f, AngleMax = 135f,
            Gravity = -26f, Drag = 0.4f, LifeMin = 0.9f, LifeMax = 1.5f, SizeMin = 0.32f, SizeMax = 0.5f, EndSize = 0.8f,
            Spin = 200f, Color = Color.white, EndColor = Color.white, Flip = true, Spread = new Vector2(0.3f, 0.3f),
        });

        public void Sparks(Vector3 pos, Color color, int count, float power = 1f, bool unscaled = false) => Emit(pos, new Burst
        {
            Sprite = "vfx_sparkle", Count = count, SpeedMin = 3f * power, SpeedMax = 10f * power, AngleMin = 0f, AngleMax = 360f,
            Gravity = -5f, Drag = 2.5f, LifeMin = 0.3f, LifeMax = 0.75f, SizeMin = 0.25f, SizeMax = 0.7f, EndSize = 0f,
            Spin = 400f, Color = color, EndColor = color, Additive = true, Unscaled = unscaled,
        });

        public void Cards(Vector3 pos, int count) => Emit(pos, new Burst
        {
            Sprite = "vfx_card", Count = count, SpeedMin = 4f, SpeedMax = 10f, AngleMin = 20f, AngleMax = 160f,
            Gravity = -14f, Drag = 0.8f, LifeMin = 1.0f, LifeMax = 1.6f, SizeMin = 0.45f, SizeMax = 0.7f, EndSize = 0.7f,
            Spin = 720f, Color = Color.white, EndColor = Color.white, Flip = true,
        });

        public void Dust(Vector3 pos, int count, Color color) => Emit(pos, new Burst
        {
            Sprite = "vfx_dust", Count = count, SpeedMin = 0.8f, SpeedMax = 3f, AngleMin = 0f, AngleMax = 180f,
            Gravity = 0.6f, Drag = 2.2f, LifeMin = 0.5f, LifeMax = 0.9f, SizeMin = 0.6f, SizeMax = 1.2f, EndSize = 2.2f,
            Spin = 90f, Color = color, EndColor = new Color(color.r, color.g, color.b, 0f), Order = OrderBehind + 20, Spread = new Vector2(0.4f, 0.05f),
        });

        /// <summary>Paper strips that tumble and flutter down.</summary>
        public void Confetti(Vector3 pos, int count) => Emit(pos, new Burst
        {
            Sprite = "vfx_confetti", Count = count, SpeedMin = 6f, SpeedMax = 15f, AngleMin = 50f, AngleMax = 130f,
            Gravity = -9f, Drag = 1.6f, LifeMin = 1.6f, LifeMax = 2.6f, SizeMin = 0.2f, SizeMax = 0.32f, EndSize = 0.9f,
            Spin = 600f, RandomHue = true, Flip = true, Spread = new Vector2(2f, 0.2f),
        });

        public void Embers(Vector3 pos, Color color, int count, Vector2 spread) => Emit(pos, new Burst
        {
            Sprite = "vfx_glow", Count = count, SpeedMin = 0.3f, SpeedMax = 1.2f, AngleMin = 60f, AngleMax = 120f,
            Gravity = 0.4f, Drag = 0.2f, LifeMin = 2.5f, LifeMax = 5f, SizeMin = 0.08f, SizeMax = 0.22f, EndSize = 0.3f,
            Color = color, EndColor = new Color(color.r, color.g, color.b, 0f), Additive = true, Order = OrderBehind, Spread = spread,
        });

        /// <summary>Coins arcing from a world point into a moving target (e.g. the HUD counter).</summary>
        public void FlyCoins(Vector3 from, Func<Vector3> to, int count, Action onEach)
        {
            for (int i = 0; i < count; i++) StartCoroutine(FlyCoin(from, to, i * 0.035f, onEach));
        }

        IEnumerator FlyCoin(Vector3 from, Func<Vector3> to, float delay, Action onArrive)
        {
            yield return new WaitForSecondsRealtime(delay);
            var p = Take(false);
            var sprite = config.Sprite("vfx_coin");
            p.Sr.sprite = sprite;
            p.Sr.sortingOrder = OrderText - 1;
            p.Sr.color = Color.white;
            float size = 0.42f / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            var start = from + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.6f);
            var ctrl = start + new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(2.5f, 5f));
            float dur = UnityEngine.Random.Range(0.45f, 0.7f);
            yield return Tween.Run(dur, t =>
            {
                var end = to();
                var a = Vector3.Lerp(start, ctrl, t);
                var b = Vector3.Lerp(ctrl, end, t);
                p.Sr.transform.position = Vector3.Lerp(a, b, t);
                float flip = Mathf.Abs(Mathf.Cos(t * 6f * Mathf.PI)) * 0.85f + 0.15f;
                p.Sr.transform.localScale = new Vector3(size * flip, size, 1f) * Mathf.Lerp(1.2f, 0.7f, t);
            }, Ease.InQuad, true);
            Release(p);
            onArrive?.Invoke();
        }

        // ---------- Damage numbers ----------

        public void Text(Vector3 pos, string text, Color color, float size = 1f, float rise = 1.4f, float life = 1.1f, bool shake = false)
        {
            var tmp = freeText.Count > 0 ? freeText.Pop() : NewText();
            tmp.gameObject.SetActive(true);
            tmp.text = text;
            tmp.color = color;
            StartCoroutine(AnimateText(tmp, pos, size, rise, life, shake));
        }

        TextMeshPro NewText()
        {
            var go = new GameObject("popup");
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = config.numberFont;
            tmp.fontSize = 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 3f);
            tmp.outlineWidth = 0.28f;
            tmp.outlineColor = new Color32(20, 8, 10, 255);
            tmp.sortingOrder = OrderText;
            return tmp;
        }

        IEnumerator AnimateText(TextMeshPro tmp, Vector3 pos, float size, float rise, float life, bool shake)
        {
            var tr = tmp.transform;
            var baseColor = tmp.color;
            float drift = UnityEngine.Random.Range(-0.4f, 0.4f);
            yield return Tween.Run(life, t =>
            {
                float pop = t < 0.18f ? Ease.OutBackBig(t / 0.18f) : 1f;
                float s = size * pop * (t > 0.75f ? Mathf.Lerp(1f, 0.6f, (t - 0.75f) / 0.25f) : 1f);
                var jitter = shake && t < 0.4f ? (Vector3)(UnityEngine.Random.insideUnitCircle * 0.12f * (1f - t / 0.4f)) : Vector3.zero;
                tr.position = pos + new Vector3(drift * t, Ease.OutCubic(t) * rise) + jitter;
                tr.localScale = Vector3.one * s;
                tr.rotation = Quaternion.Euler(0, 0, Mathf.Lerp(drift * -12f, 0f, Mathf.Min(1f, t * 4f)));
                var c = baseColor;
                c.a = t > 0.75f ? 1f - (t - 0.75f) / 0.25f : 1f;
                tmp.color = c;
            }, Ease.Linear, true);
            tmp.gameObject.SetActive(false);
            freeText.Push(tmp);
        }
    }
}
