using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Video;

namespace Lucky
{
    /// <summary>Arenas laid out left to right; the hero runs between them. Seams hide behind dark gaps and parallax pillars.</summary>
    public class World : MonoBehaviour
    {
        public const float Spacing = 23.5f;
        public const float GroundY = -2.75f;
        public const float HeroX = -6.4f, BossX = 6.5f;
        const float BgHeight = 11.9f;

        [System.Serializable] class LightSpot { public float u, v, size; public float[] color; }
        [System.Serializable] class ArenaLights { public string background; public LightSpot[] lights; }
        [System.Serializable] class LightSheet { public ArenaLights[] arenas; }

        struct Flicker { public Light2D Light; public Vector3 Home; public float Base, Seed; }

        /// <summary>Animated backdrop: a lit quad fed by a looping video, fading in over the still once frames arrive.</summary>
        class LiveBackdrop
        {
            public VideoPlayer Player;
            public Material Material;
            public float X, Alpha;
            public bool Failed;
        }

        readonly List<Transform> pillars = new();
        readonly List<float> seams = new();
        readonly List<Color> ambient = new();
        readonly List<Flicker> flickers = new();
        readonly List<LiveBackdrop> backdrops = new();
        GameConfig config;
        float emberTimer;

        public static float ArenaX(int index) => index * Spacing;

        /// <summary>0..1 how far the backdrops near the camera have faded into live video; failed videos count as done.</summary>
        public float BackdropProgress
        {
            get
            {
                float sum = 0f;
                int count = 0;
                foreach (var b in backdrops)
                {
                    if (Mathf.Abs(b.X - CameraRig.I.BaseX) >= Spacing) continue;
                    sum += b.Failed ? 1f : b.Alpha;
                    count++;
                }
                return count == 0 ? 1f : sum / count;
            }
        }

        public void Build(GameConfig cfg, List<BossDef> bosses)
        {
            config = cfg;
            var sheet = JsonUtility.FromJson<LightSheet>(Resources.Load<TextAsset>("Data/lights")?.text ?? "{}");
            for (int i = 0; i < bosses.Count; i++)
            {
                float x = ArenaX(i);
                var bg = Sprite($"Arena{i}", bosses[i].Background, new Vector3(x, 0f), -100, cfg.spriteLit);
                float bgWidth = BgHeight * 16f / 9f;
                if (bg.sprite != null)
                {
                    float s = BgHeight / bg.sprite.bounds.size.y;
                    bg.transform.localScale = new Vector3(s, s, 1f);
                    bgWidth = bg.sprite.bounds.size.x * s;
                }
                // Push the backdrop back so characters pop.
                bg.color = new Color(0.86f, 0.84f, 0.88f);
                bg.sortingOrder = -101;
                // Headless/batch editors can't decode video reliably (and it destabilised the shared GPU), so they keep the stills.
                if (bg.sprite != null && !Application.isBatchMode)
                    backdrops.Add(CreateBackdrop(bosses[i].Background, bg.sprite, new Vector3(x, 0f), bgWidth, bg.color));

                var spots = sheet.arenas?.FirstOrDefault(a => a.background == bosses[i].Background)?.lights;
                if (spots != null)
                    foreach (var spot in spots)
                    {
                        var pos = new Vector3(x + (spot.u - 0.5f) * bgWidth, (0.5f - spot.v) * BgHeight, 0f);
                        var color = new Color(spot.color[0], spot.color[1], spot.color[2]);
                        var light = Instantiate(cfg.lightPrefab, pos, Quaternion.identity, transform).GetComponent<Light2D>();
                        light.color = Color.Lerp(color, Color.white, 0.15f);
                        light.pointLightOuterRadius = Mathf.Clamp(spot.size * bgWidth * 9f, 3f, 7.5f);
                        light.intensity = 1.1f;
                        flickers.Add(new Flicker { Light = light, Home = pos, Base = light.intensity, Seed = Random.value * 50f });
                    }

                foreach (float side in new[] { -1f, 1f })
                {
                    var fade = Sprite($"Fade{i}{side}", "edge_fade", new Vector3(x + side * (bgWidth * 0.5f - 1.6f), 0f), -99, cfg.spriteDefault);
                    if (fade.sprite == null) continue;
                    fade.transform.localScale = new Vector3(3.3f / fade.sprite.bounds.size.x * -side, BgHeight / fade.sprite.bounds.size.y, 1f);
                }

                ambient.Add(Color.Lerp(bosses[i].Accent, new Color(1f, 0.7f, 0.35f), 0.5f));
                if (i == bosses.Count - 1) continue;
                float seam = x + Spacing * 0.5f;
                seams.Add(seam);
                var pillar = Sprite($"Pillar{i}", "fg_pillar", new Vector3(seam, 0f), 300, cfg.spriteLit);
                if (pillar.sprite != null)
                {
                    float ps = 12.5f / pillar.sprite.bounds.size.y;
                    pillar.transform.localScale = new Vector3(ps, ps, 1f);
                }
                pillar.color = new Color(0.55f, 0.5f, 0.5f);
                pillars.Add(pillar.transform);
            }
        }

        LiveBackdrop CreateBackdrop(string name, Sprite still, Vector3 pos, float width, Color tint)
        {
            // Hand-built quad: CreatePrimitive would add a MeshCollider, which WebGL builds strip.
            var quad = new GameObject(name + "_video");
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = pos;
            quad.transform.localScale = new Vector3(width, BgHeight, 1f);
            quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sortingOrder = -100;
            var mat = new Material(config.videoLit);
            var normals = new SecondarySpriteTexture[still.GetSecondaryTextureCount()];
            still.GetSecondaryTextures(normals);
            foreach (var n in normals)
                if (n.name == "_NormalMap") mat.SetTexture("_NormalMap", n.texture);
            mat.SetColor("_Color", tint);
            mat.SetFloat("_Alpha", 0f);
            mr.sharedMaterial = mat;

            var rt = new RenderTexture(1600, 896, 0) { name = name + "_rt" };
            var vp = quad.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = true;
            vp.skipOnDrop = true;
            vp.source = VideoSource.Url;
            vp.url = Application.streamingAssetsPath + "/Video/" + name + ".mp4";
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = rt;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            var backdrop = new LiveBackdrop { Player = vp, Material = mat, X = pos.x };
            vp.errorReceived += (_, msg) =>
            {
                backdrop.Failed = true;
                Debug.LogWarning($"Backdrop video {name}: {msg}");
            };
            mat.mainTexture = rt;
            vp.Prepare();
            return backdrop;
        }

        static Mesh quadMesh;

        static Mesh QuadMesh()
        {
            if (quadMesh != null) return quadMesh;
            quadMesh = new Mesh { name = "BackdropQuad" };
            quadMesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
            quadMesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            quadMesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            var tangent = new Vector4(1f, 0f, 0f, -1f);
            quadMesh.SetTangents(new[] { tangent, tangent, tangent, tangent });
            quadMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            return quadMesh;
        }

        SpriteRenderer Sprite(string name, string sprite, Vector3 pos, int order, Material material)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = pos;
            sr.sprite = config.Sprite(sprite);
            sr.sharedMaterial = material;
            sr.sortingOrder = order;
            return sr;
        }

        void LateUpdate()
        {
            var cam = CameraRig.I;
            if (cam == null) return;
            float camX = cam.BaseX;
            // Foreground pillars move faster than the camera for depth.
            for (int i = 0; i < pillars.Count; i++)
                pillars[i].position = new Vector3(camX + (seams[i] - camX) * 1.6f, 0f, 0f);

            // Only backdrops near the camera decode video; each fades in once it has real frames.
            foreach (var b in backdrops)
            {
                bool near = Mathf.Abs(b.X - camX) < Spacing;
                if (near && b.Player.isPrepared && !b.Player.isPlaying) b.Player.Play();
                else if (!near && b.Player.isPlaying) b.Player.Pause();
                float target = b.Player.isPlaying && b.Player.frame > 1 ? 1f : 0f;
                if (Mathf.Abs(b.Alpha - target) < 0.001f) continue;
                b.Alpha = Mathf.MoveTowards(b.Alpha, target, Time.unscaledDeltaTime * 2f);
                b.Material.SetFloat("_Alpha", b.Alpha);
            }

            // Fire flicker: layered noise on every torch/candle light near the camera, with a small flame sway.
            float t = Time.time;
            foreach (var f in flickers)
            {
                bool near = Mathf.Abs(f.Light.transform.position.x - camX) < 16f;
                f.Light.enabled = near;
                if (!near) continue;
                float n = Mathf.PerlinNoise(f.Seed, t * 7f) * 0.6f + Mathf.PerlinNoise(f.Seed + 3f, t * 19f) * 0.4f;
                f.Light.intensity = f.Base * (0.7f + n * 0.6f);
                f.Light.transform.position = f.Home + new Vector3(Mathf.PerlinNoise(f.Seed + 7f, t * 3f) - 0.5f, Mathf.PerlinNoise(f.Seed + 11f, t * 4f) - 0.5f) * 0.35f;
            }

            emberTimer -= Time.deltaTime;
            if (emberTimer > 0f || FX.I == null) return;
            emberTimer = 0.18f;
            int arena = Mathf.Clamp(Mathf.RoundToInt(camX / Spacing), 0, ambient.Count - 1);
            FX.I.Embers(new Vector3(camX, -5.2f), ambient[arena], 1, new Vector2(9f, 0.3f));
        }
    }
}
