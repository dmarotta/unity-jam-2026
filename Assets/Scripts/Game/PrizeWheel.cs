using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lucky
{
    /// <summary>
    /// The wheel of fate. The result is chosen before the spin; the animation lands on it with flapper physics,
    /// peg ticks, gem lights and near-miss drama. Angle decreases while spinning (clockwise on screen).
    /// </summary>
    public class PrizeWheel : MonoBehaviour
    {
        public const float FaceRadius = 3.3f;
        const float RimHoleRatio = 0.725f, GemRadiusRatio = 0.857f, LabelRadius = 0.71f;
        const int Order = 100;

        public enum Drama { Center, TipIn, EdgeOut, RockBack }
        public enum Lights { Idle, Spin, Jackpot, Bust }
        enum State { Normal, Kill, Bust }

        static readonly Color[] Palette =
        {
            new(0.62f, 0.10f, 0.16f), // crimson
            new(0.12f, 0.16f, 0.30f), // midnight
            new(0.91f, 0.83f, 0.64f), // parchment
        };
        static readonly Color Gold = new(1f, 0.80f, 0.32f);

        GameConfig config;
        int[] values;
        int count;
        float slice;
        State[] states;
        int killSlice = -1;
        int heat;

        Transform disc, pointer;
        TextMeshPro[] labels;
        SpriteRenderer[] icons, gems;
        SpriteRenderer backGlow, pointerGlow;
        MeshRenderer killOverlay, winOverlay, blur;
        Material killMat, winMat;
        float winGlow;
        MeshFilter bustFilter;
        Mesh blurMesh;
        Color[] blurColors, blurScratch;
        float rimOuter;

        float prevAngle, flap, flapVel, labelFade = 1f;
        int lastPeg;
        bool fastForward;
        Lights lights = Lights.Idle;
        float lightsUntil, chase;
        Spring punch;

        public float Angle { get; private set; }
        public float Speed { get; private set; }
        public bool Spinning { get; private set; }
        public int KillSlice => killSlice;
        public int Count => count;

        /// <summary>Fired once per frame in which pegs crossed the flapper; argument is speed in deg/s.</summary>
        public event Action<float> PegCrossed;

        public int ValueAt(int slot) => values[slot];
        public int SliceAtPointer() => Mod(Mathf.RoundToInt(Angle / slice), count);
        public Vector3 PointerTip => pointer.position + pointer.rotation * new Vector3(0f, -1.25f, 0f);
        public Vector3 Center => transform.position;
        public void FastForward() => fastForward = true;

        static int Mod(int a, int m) => (a % m + m) % m;
        static float Mod(float a, float m) => (a % m + m) % m;

        public void Build(GameConfig cfg, int[] slotValues)
        {
            config = cfg;
            values = slotValues;
            count = values.Length;
            slice = 360f / count;
            states = new State[count];

            disc = new GameObject("Disc").transform;
            disc.SetParent(transform, false);

            backGlow = Sprite("BackGlow", transform, "vfx_glow", Vector3.zero, 15f, Order - 2, cfg.additive);
            backGlow.color = new Color(1f, 0.6f, 0.25f, 0.25f);

            var face = MeshObject("Face", disc, Order, out var faceFilter);
            face.sharedMaterial = new Material(cfg.vertexColor);
            face.sharedMaterial.mainTexture = cfg.Sprite("wheel_detail")?.texture;
            face.sharedMaterial.SetFloat("_DetailStrength", 0.32f);
            faceFilter.sharedMesh = BuildFace();

            var bust = MeshObject("BustShade", disc, Order + 1, out bustFilter);
            bust.sharedMaterial = new Material(cfg.vertexColor);
            bust.sharedMaterial.SetFloat("_DetailStrength", 0f);
            bustFilter.sharedMesh = new Mesh();

            killOverlay = MeshObject("KillGlow", disc, Order + 2, out var killFilter);
            killMat = new Material(cfg.vertexColor);
            killMat.SetFloat("_DetailStrength", 0f);
            killMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            killMat.SetFloat("_DstBlend", (float)BlendMode.One);
            killOverlay.sharedMaterial = killMat;
            var killMesh = new Mesh();
            AddWedge(new List<Vector3>(), new List<Color>(), new List<int>(), 0, 0.16f, 0.99f, Gold, killMesh);
            killFilter.sharedMesh = killMesh;
            killOverlay.gameObject.SetActive(false);

            winOverlay = MeshObject("WinGlow", disc, Order + 3, out var winFilter);
            winMat = new Material(killMat);
            winOverlay.sharedMaterial = winMat;
            var winMesh = new Mesh();
            AddWedge(new List<Vector3>(), new List<Color>(), new List<int>(), 0, 0.12f, 1f, new Color(1f, 0.95f, 0.8f), winMesh);
            winFilter.sharedMesh = winMesh;
            winOverlay.gameObject.SetActive(false);

            labels = new TextMeshPro[count];
            icons = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                float a = SliceAngle(i);
                var dir = Dir(a);
                var label = new GameObject($"Label{i}").AddComponent<TextMeshPro>();
                label.transform.SetParent(disc, false);
                label.transform.localPosition = dir * FaceRadius * LabelRadius;
                label.transform.localRotation = Quaternion.Euler(0, 0, a - 90f);
                label.font = cfg.numberFont;
                label.fontSize = 9f;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.sizeDelta = new Vector2(2.4f, 1.6f);
                label.text = values[i].ToString();
                label.outlineWidth = 0.22f;
                label.outlineColor = new Color32(18, 8, 10, 255);
                label.sortingOrder = Order + 4;
                labels[i] = label;

                icons[i] = Sprite($"Icon{i}", disc, "icon_crown", dir * FaceRadius * 0.45f, 0.62f, Order + 4, cfg.spriteDefault);
                icons[i].transform.localRotation = Quaternion.Euler(0, 0, a - 90f);
                icons[i].enabled = false;

                float pegAngle = a + slice * 0.5f;
                var pegPos = Dir(pegAngle) * FaceRadius * 0.955f;
                Sprite($"PegShadow{i}", disc, "vfx_circle", pegPos, 0.27f, Order + 5, cfg.spriteDefault).color = new Color(0.12f, 0.05f, 0.02f, 1f);
                Sprite($"Peg{i}", disc, "vfx_circle", pegPos, 0.19f, Order + 6, cfg.spriteDefault).color = Gold;
            }

            Sprite("Hub", disc, "wheel_hub", Vector3.zero, 1.6f, Order + 8, cfg.spriteDefault);

            blur = MeshObject("Blur", transform, Order + 7, out var blurFilter);
            var blurMat = new Material(cfg.vertexColor);
            blurMat.SetFloat("_DetailStrength", 0f);
            blur.sharedMaterial = blurMat;
            blurMesh = new Mesh();
            AddWedge(new List<Vector3>(), new List<Color>(), new List<int>(), 0, 0f, 1f, new Color(0.3f, 0.12f, 0.14f), blurMesh, 360f, 72);
            blurFilter.sharedMesh = blurMesh;
            blurColors = blurMesh.colors;
            blurScratch = new Color[blurColors.Length];

            // Static lacquer sheen over the spinning face (does not rotate, so it reads as reflected light).
            Sprite("Gloss", transform, "wheel_gloss", Vector3.zero, FaceRadius * 2f, Order + 9, cfg.spriteDefault).color = new Color(1f, 0.95f, 0.85f, 0.85f);

            rimOuter = (FaceRadius + 0.08f) / RimHoleRatio;
            Sprite("Rim", transform, "wheel_rim", Vector3.zero, rimOuter * 2f, Order + 10, cfg.spriteDefault);

            gems = new SpriteRenderer[12];
            for (int i = 0; i < gems.Length; i++)
                gems[i] = Sprite($"Gem{i}", transform, "bulb_glow", Dir(i * 30f) * rimOuter * GemRadiusRatio, 1.05f, Order + 11, cfg.additive);

            pointerGlow = Sprite("PointerGlow", transform, "vfx_glow", new Vector3(0f, rimOuter * 0.86f - 0.5f), 2.6f, Order + 12, cfg.additive);
            pointerGlow.color = new Color(1f, 0.8f, 0.3f, 0f);
            pointer = new GameObject("Pointer").transform;
            pointer.SetParent(transform, false);
            pointer.localPosition = new Vector3(0f, rimOuter * 0.86f + 0.25f, 0f);
            var pointerSprite = Sprite("PointerSprite", pointer, "wheel_pointer", Vector3.zero, 1.3f, Order + 13, cfg.spriteDefault);
            pointerSprite.transform.localScale = new Vector3(pointerSprite.transform.localScale.x * 0.85f, pointerSprite.transform.localScale.y, 1f);

            ApplyRotation();
        }

        // ---------- Construction helpers ----------

        float SliceAngle(int i) => 90f - i * slice;
        static Vector3 Dir(float deg) => new(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad), 0f);

        SpriteRenderer Sprite(string name, Transform parent, string sprite, Vector3 pos, float size, int order, Material mat)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.transform.localPosition = pos;
            sr.sprite = config.Sprite(sprite);
            sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            if (sr.sprite != null)
            {
                float s = size / Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
                sr.transform.localScale = new Vector3(s, s, 1f);
            }
            return sr;
        }

        static MeshRenderer MeshObject(string name, Transform parent, int order, out MeshFilter filter)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sortingOrder = order;
            return mr;
        }

        Mesh BuildFace()
        {
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            float[] rings = { 0f, 0.22f, 0.55f, 0.82f, 0.93f, 1f };
            float[] shade = { 0.42f, 0.55f, 0.82f, 1.0f, 0.92f, 0.62f };
            const int seg = 10;
            for (int i = 0; i < count; i++)
            {
                var col = Palette[i % Palette.Length];
                float a0 = SliceAngle(i) + slice * 0.5f;
                for (int r = 0; r < rings.Length - 1; r++)
                {
                    for (int s = 0; s < seg; s++)
                    {
                        float aa = a0 - slice * s / seg, ab = a0 - slice * (s + 1) / seg;
                        int b = v.Count;
                        v.Add(Dir(aa) * rings[r] * FaceRadius); c.Add(col * shade[r]);
                        v.Add(Dir(ab) * rings[r] * FaceRadius); c.Add(col * shade[r]);
                        v.Add(Dir(ab) * rings[r + 1] * FaceRadius); c.Add(col * shade[r + 1]);
                        v.Add(Dir(aa) * rings[r + 1] * FaceRadius); c.Add(col * shade[r + 1]);
                        t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    }
                }
            }
            // Ink outline then gold separators on top.
            for (int i = 0; i < count; i++)
            {
                float a = SliceAngle(i) + slice * 0.5f;
                AddBar(v, c, t, a, 0.14f, 0.99f, 0.12f, new Color(0.08f, 0.03f, 0.02f, 1f));
                AddBar(v, c, t, a, 0.15f, 0.98f, 0.05f, Gold * 0.95f);
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, v.ConvertAll(p => new Vector2(p.x / (2f * FaceRadius) + 0.5f, p.y / (2f * FaceRadius) + 0.5f)));
            mesh.SetTriangles(t, 0);
            return mesh;
        }

        static void AddBar(List<Vector3> v, List<Color> c, List<int> t, float deg, float r0, float r1, float width, Color col)
        {
            var d = Dir(deg);
            var n = new Vector3(-d.y, d.x) * width * 0.5f;
            int b = v.Count;
            v.Add(d * r0 * FaceRadius - n); v.Add(d * r0 * FaceRadius + n);
            v.Add(d * r1 * FaceRadius + n); v.Add(d * r1 * FaceRadius - n);
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }

        void AddWedge(List<Vector3> v, List<Color> c, List<int> t, float centerDeg, float r0, float r1, Color col, Mesh target, float span = -1f, int seg = 10)
        {
            if (span < 0f) span = slice;
            for (int s = 0; s < seg; s++)
            {
                float aa = centerDeg + span * 0.5f - span * s / seg, ab = centerDeg + span * 0.5f - span * (s + 1) / seg;
                int b = v.Count;
                var inner = new Color(col.r, col.g, col.b, col.a * 0.35f);
                v.Add(Dir(aa) * r0 * FaceRadius); c.Add(inner);
                v.Add(Dir(ab) * r0 * FaceRadius); c.Add(inner);
                v.Add(Dir(ab) * r1 * FaceRadius); c.Add(col);
                v.Add(Dir(aa) * r1 * FaceRadius); c.Add(col);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            if (target == null) return;
            target.Clear();
            target.SetVertices(v);
            target.SetColors(c);
            target.SetTriangles(t, 0);
        }

        // ---------- State ----------

        /// <summary>Recolor slices for the boss's remaining HP: exact-kill, bust and normal.</summary>
        public void SetTarget(int hp, int misses)
        {
            heat = misses;
            killSlice = -1;
            var v = new List<Vector3>();
            var c = new List<Color>();
            var t = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var previous = states[i];
                states[i] = values[i] == hp ? State.Kill : values[i] > hp ? State.Bust : State.Normal;
                if (states[i] == State.Kill) killSlice = i;
                if (states[i] == State.Bust) AddWedge(v, c, t, SliceAngle(i), 0.12f, 1f, new Color(0.05f, 0f, 0.02f, 0.62f), null);

                var label = labels[i];
                var baseCol = i % Palette.Length == 2 ? new Color(0.24f, 0.07f, 0.07f) : new Color(1f, 0.95f, 0.84f);
                label.color = states[i] switch
                {
                    State.Kill => new Color(1f, 0.86f, 0.3f),
                    State.Bust => Color.Lerp(baseCol, new Color(0.55f, 0.3f, 0.32f), 0.6f),
                    _ => baseCol,
                };
                icons[i].enabled = states[i] != State.Normal;
                icons[i].sprite = config.Sprite(states[i] == State.Kill ? "icon_crown" : "icon_bust");
                float iconSize = states[i] == State.Kill ? 0.7f : 0.42f;
                if (icons[i].sprite != null)
                {
                    float s = iconSize / Mathf.Max(icons[i].sprite.bounds.size.x, icons[i].sprite.bounds.size.y);
                    icons[i].transform.localScale = new Vector3(s, s, 1f);
                }
                if (previous != states[i]) StartCoroutine(PopLabel(label.transform, states[i] == State.Kill ? 1.45f : 1.15f));
            }
            var bustMesh = bustFilter.sharedMesh;
            bustMesh.Clear();
            bustMesh.SetVertices(v);
            bustMesh.SetColors(c);
            bustMesh.SetTriangles(t, 0);

            killOverlay.gameObject.SetActive(killSlice >= 0);
            if (killSlice >= 0) killOverlay.transform.localRotation = Quaternion.Euler(0, 0, SliceAngle(killSlice));
        }

        IEnumerator PopLabel(Transform label, float amount)
        {
            yield return Tween.Run(0.45f, t => label.localScale = Vector3.one * Mathf.Lerp(amount, 1f, Ease.OutElastic(t)), Ease.Linear, true);
        }

        public void SetLights(Lights mode, float duration = 0f)
        {
            lights = mode;
            lightsUntil = duration > 0f ? Time.unscaledTime + duration : float.MaxValue;
        }

        /// <summary>Scale punch of the whole wheel (land, spin start).</summary>
        public void Punch(float amount) => punch.Velocity += amount * 30f;

        public void Flare(Color color, float alpha = 0.9f) => backGlow.color = new Color(color.r, color.g, color.b, alpha);

        /// <summary>Flash the landed slice so edge landings read instantly.</summary>
        public void HighlightWin(int slot)
        {
            winOverlay.transform.localRotation = Quaternion.Euler(0, 0, SliceAngle(slot));
            winOverlay.gameObject.SetActive(true);
            winGlow = 1f;
            StartCoroutine(PopLabel(labels[slot].transform, 1.6f));
        }

        // ---------- Spin ----------

        public IEnumerator Spin(int target, float duration, Drama drama)
        {
            Spinning = true;
            fastForward = false;
            float half = slice * 0.5f;
            float offset = drama switch
            {
                Drama.TipIn => half - UnityEngine.Random.Range(1.0f, 3.0f),
                Drama.EdgeOut => -half + UnityEngine.Random.Range(1.0f, 2.5f),
                Drama.RockBack => -half - UnityEngine.Random.Range(1.5f, 3.0f),
                _ => UnityEngine.Random.Range(-half * 0.6f, half * 0.6f),
            };
            float start = Angle;
            float total = UnityEngine.Random.Range(3, 5) * 360f + Mod(start - (target * slice + offset), 360f);
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / duration * (fastForward ? 4.5f : 1f));
                Angle = start - total * (1f - Mathf.Pow(1f - t, 3f));
                yield return null;
            }
            if (drama == Drama.RockBack)
            {
                yield return new WaitForSeconds(fastForward ? 0.05f : 0.18f);
                float from = Angle, to = target * slice - half + UnityEngine.Random.Range(1.5f, 3f);
                yield return Tween.Run(0.42f, k => Angle = Mathf.LerpUnclamped(from, to, k), Ease.OutBack);
            }
            Spinning = false;
        }

        // ---------- Per-frame visuals ----------

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            float delta = Angle - prevAngle;
            Speed = dt > 0f ? Mathf.Abs(delta) / dt : 0f;
            ApplyRotation();
            UpdatePegs();
            UpdateFlapper(Mathf.Sign(delta) * (Mathf.Abs(delta) > 0.0001f ? 1f : 0f), Mathf.Max(dt, 0.0001f));
            prevAngle = Angle;

            // Speed blur hides high-contrast stripes while spinning fast.
            float blurAmount = Mathf.InverseLerp(250f, 1100f, Speed);
            blur.enabled = blurAmount > 0.01f;
            if (blur.enabled) SetBlurAlpha(blurAmount * 0.7f);
            float fade = 1f - blurAmount * 0.85f;
            if (Mathf.Abs(fade - labelFade) > 0.02f)
            {
                labelFade = fade;
                foreach (var l in labels) l.alpha = fade;
            }

            punch.Step(0f, 220f, 11f, udt);
            transform.localScale = Vector3.one * (1f + punch.Value);

            float now = Time.unscaledTime;
            if (now > lightsUntil) lights = Lights.Idle;
            UpdateGems(now);

            if (killSlice >= 0)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(now * (5f + heat * 1.5f));
                killMat.SetFloat("_Intensity", 0.5f + pulse * 0.6f + Mathf.Min(heat, 6) * 0.12f);
            }
            var bg = backGlow.color;
            bg.a = Mathf.MoveTowards(bg.a, killSlice >= 0 ? 0.32f + 0.08f * Mathf.Sin(now * 3f) : 0.2f, udt * 0.8f);
            backGlow.color = bg;
            var pg = pointerGlow.color;
            pg.a = Mathf.MoveTowards(pg.a, 0f, udt * 2.5f);
            pointerGlow.color = pg;

            if (winGlow > 0f)
            {
                winGlow = Mathf.Max(0f, winGlow - udt / 1.4f);
                float blink = 0.6f + 0.4f * Mathf.Cos((1f - winGlow) * Mathf.PI * 6f);
                winMat.SetFloat("_Intensity", winGlow * blink * 1.6f);
                if (winGlow <= 0f) winOverlay.gameObject.SetActive(false);
            }
        }

        void SetBlurAlpha(float a)
        {
            for (int i = 0; i < blurColors.Length; i++)
                blurScratch[i] = new Color(blurColors[i].r, blurColors[i].g, blurColors[i].b, blurColors[i].a * a);
            blurMesh.colors = blurScratch;
        }

        void ApplyRotation() => disc.localRotation = Quaternion.Euler(0f, 0f, Angle);

        void UpdatePegs()
        {
            int peg = Mathf.FloorToInt((Angle - slice * 0.5f) / slice);
            if (peg == lastPeg) return;
            lastPeg = peg;
            chase += 1f;
            var pg = pointerGlow.color;
            pg.a = Mathf.Min(1f, pg.a + 0.35f);
            pointerGlow.color = pg;
            PegCrossed?.Invoke(Speed);
        }

        void UpdateFlapper(float dir, float dt)
        {
            const float contact = 6.5f, maxDeflect = 28f;
            float d1 = Mod(Angle - slice * 0.5f, slice);
            float target = 0f;
            if (dir < 0f && d1 < contact) target = maxDeflect * (1f - d1 / contact);
            else if (dir > 0f && slice - d1 < contact) target = -maxDeflect * (1f - (slice - d1) / contact);

            if (target > 0f && target > flap || target < 0f && target < flap)
            {
                flap = target;
                flapVel = 0f;
            }
            else
            {
                flapVel += (-1100f * flap - 18f * flapVel) * dt;
                flap += flapVel * dt;
            }
            pointer.localRotation = Quaternion.Euler(0f, 0f, flap);
        }

        void UpdateGems(float now)
        {
            for (int i = 0; i < gems.Length; i++)
            {
                Color c;
                switch (lights)
                {
                    case Lights.Spin:
                    {
                        int lit = Mod((int)chase, gems.Length);
                        bool on = i == lit || i == Mod(lit + 6, gems.Length) || i == Mod(lit + 3, gems.Length) || i == Mod(lit + 9, gems.Length);
                        c = on ? new Color(1f, 0.85f, 0.45f, 1f) : new Color(1f, 0.5f, 0.2f, 0.18f);
                        break;
                    }
                    case Lights.Jackpot:
                        c = Color.HSVToRGB(Mathf.Repeat(now * 1.5f + i / 12f, 1f), 0.6f, 1f);
                        c.a = (int)(now * 6f + i) % 2 == 0 ? 1f : 0.45f;
                        break;
                    case Lights.Bust:
                        c = new Color(1f, 0.15f, 0.2f, (int)(now * 5f) % 2 == 0 ? 1f : 0.2f);
                        break;
                    default:
                    {
                        float hot = killSlice >= 0 ? 1f : 0f;
                        float wave = 0.5f + 0.5f * Mathf.Sin(now * (2f + hot * 4f) - i * 0.52f);
                        c = Color.Lerp(new Color(1f, 0.62f, 0.3f), new Color(1f, 0.88f, 0.4f), hot);
                        c.a = 0.25f + wave * (0.45f + hot * 0.3f);
                        break;
                    }
                }
                gems[i].color = c;
            }
        }
    }
}
