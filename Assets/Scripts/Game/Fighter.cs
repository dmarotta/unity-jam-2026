using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Lucky
{
    public enum Pose { Idle, Attack, Hurt, Run, Victory, Death }

    /// <summary>A side-view character: key-pose sprites animated procedurally with springs and shader effects.</summary>
    public class Fighter : MonoBehaviour
    {
        // Poses are generated as separate images; these ratios keep the character's on-screen size consistent.
        static readonly Dictionary<Pose, float> PoseHeight = new()
        {
            { Pose.Idle, 1f }, { Pose.Attack, 0.9f }, { Pose.Hurt, 0.96f }, { Pose.Run, 0.98f }, { Pose.Victory, 1.1f }, { Pose.Death, 0.6f },
        };

        public SpriteRenderer Body { get; private set; }
        public float Height { get; private set; }
        public Vector3 Home;
        public Color Accent = Color.white;

        readonly Dictionary<Pose, Sprite> poses = new();
        readonly Dictionary<Pose, Flipbook> anims = new();
        Material mat;
        SpriteRenderer shadow, ghost;
        Transform pivot;
        Spring squashX, squashY, offsetX, offsetY, tilt;
        float flash, poseUntil, animTime, ghostLife;
        Pose pose;
        Flipbook anim;

        public Vector3 Chest => transform.position + new Vector3(0f, Height * 0.55f, 0f);
        public Vector3 Head => transform.position + new Vector3(0f, Height * 0.85f, 0f);

        public static Fighter Create(string name, Transform parent, GameConfig cfg, Dictionary<Pose, Sprite> sprites, Dictionary<Pose, string> flipbooks, float height, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<Fighter>();
            f.Height = height;
            foreach (var kv in sprites)
                if (kv.Value != null) f.poses[kv.Key] = kv.Value;
            foreach (var kv in flipbooks)
            {
                var book = Flipbook.Load(kv.Value);
                if (book != null) f.anims[kv.Key] = book;
            }

            f.shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            f.shadow.transform.SetParent(go.transform, false);
            f.shadow.sprite = cfg.Sprite("shadow");
            f.shadow.sharedMaterial = cfg.spriteDefault;
            f.shadow.sortingOrder = order - 2;
            f.shadow.color = new Color(0f, 0f, 0f, 0.7f);

            f.pivot = new GameObject("Pivot").transform;
            f.pivot.SetParent(go.transform, false);
            f.Body = new GameObject("Body").AddComponent<SpriteRenderer>();
            f.Body.transform.SetParent(f.pivot, false);
            f.mat = new Material(cfg.spriteFX);
            f.mat.SetFloat("_Phase", Random.Range(0f, 10f));
            f.mat.SetFloat("_Sway", 0.035f);
            f.mat.SetFloat("_Breath", 0.014f);
            f.Body.sharedMaterial = f.mat;
            f.Body.sortingOrder = order;
            f.ghost = new GameObject("PoseGhost").AddComponent<SpriteRenderer>();
            f.ghost.transform.SetParent(f.pivot, false);
            f.ghost.sharedMaterial = cfg.spriteDefault;
            f.ghost.sortingOrder = order + 1;
            f.ghost.enabled = false;
            f.squashX.Value = f.squashY.Value = 1f;
            f.SetPose(Pose.Idle, blend: false);
            return f;
        }

        /// <summary>Seconds from the start of the attack until its impact frame.</summary>
        public float AttackHitTime => anims.TryGetValue(Pose.Attack, out var a) ? a.HitTime : 0.12f;
        public float AttackDuration => anims.TryGetValue(Pose.Attack, out var a) ? a.Duration : 0.5f;
        public bool HasAnim(Pose p) => anims.ContainsKey(p);
        public float AnimDuration(Pose p) => anims.TryGetValue(p, out var a) ? a.Duration : 0f;

        /// <summary>
        /// Switch key pose. With blend, the old pose lingers as a fading, swelling ghost while the new one
        /// pops in with a white flash and squash — a snappy anime cut instead of a sprite swap.
        /// </summary>
        public void SetPose(Pose p, float hold = 0f, bool blend = true)
        {
            if (blend && p != pose && Body.sprite != null)
            {
                ghost.sprite = Body.sprite;
                ghost.transform.localPosition = Body.transform.localPosition;
                ghost.transform.localScale = Body.transform.localScale;
                ghost.enabled = true;
                ghostLife = 1f;
                Flash(Color.white, 0.65f);
                Squash(1.12f, 0.9f);
            }
            pose = p;
            poseUntil = hold > 0f ? Time.time + hold : float.MaxValue;
            if (anims.TryGetValue(p, out anim))
            {
                animTime = 0f;
                mat.SetFloat("_Sway", 0f);
                mat.SetFloat("_Breath", 0f);
                ShowSprite(anim.Frames[0], Height / anim.BodyHeightUnits);
                if (hold <= 0f && !anim.Loop) poseUntil = Time.time + anim.Duration;
                return;
            }
            if (!poses.TryGetValue(p, out var sprite)) { pose = Pose.Idle; sprite = poses.GetValueOrDefault(Pose.Idle); }
            if (sprite == null) return;
            mat.SetFloat("_Sway", 0.035f);
            mat.SetFloat("_Breath", 0.014f);
            ShowSprite(sprite, Height * PoseHeight[pose] / sprite.bounds.size.y);
        }

        void ShowSprite(Sprite sprite, float scale)
        {
            Body.sprite = sprite;
            Body.transform.localScale = new Vector3(scale, scale, 1f);
            float width = Mathf.Min(sprite.bounds.size.x * scale, Height * 0.9f);
            float sh = width * 1.1f / shadow.sprite.bounds.size.x;
            shadow.transform.localScale = new Vector3(sh, sh * 0.22f, 1f);
        }

        public void Squash(float x, float y)
        {
            squashX.Value = x;
            squashY.Value = y;
        }

        public void Knock(float x, float y = 0f)
        {
            offsetX.Velocity += x;
            offsetY.Velocity += y;
        }

        public void Tilt(float degrees) => tilt.Velocity += degrees * 12f;

        public void Flash(Color color, float amount = 1f)
        {
            mat.SetColor("_FlashColor", color);
            flash = Mathf.Max(flash, amount);
        }

        public void SetGlow(float glow) => mat.SetFloat("_Glow", glow);
        public void SetSway(float sway, float breath) { mat.SetFloat("_Sway", sway); mat.SetFloat("_Breath", breath); }

        /// <summary>Instant silhouette (impact frames): 1 = solid color, 0 = normal.</summary>
        public void Silhouette(Color color, bool on)
        {
            mat.SetColor("_FlashColor", color);
            mat.SetFloat("_FlashAmount", on ? 1f : 0f);
            flash = on ? 1f : 0f;
        }

        public IEnumerator Dissolve(float duration, Color edge)
        {
            mat.SetColor("_DissolveEdge", edge * 4f);
            shadow.enabled = false;
            yield return Tween.Run(duration, t => mat.SetFloat("_Dissolve", t), Ease.InQuad);
            gameObject.SetActive(false);
        }

        public void ResetVisuals()
        {
            gameObject.SetActive(true);
            mat.SetFloat("_Dissolve", 0f);
            mat.SetFloat("_Glow", 1f);
            shadow.enabled = true;
            flash = 0f;
            Body.color = Color.white;
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            squashX = squashY = default;
            squashX.Value = squashY.Value = 1f;
            offsetX = offsetY = tilt = default;
            ghostLife = 0f;
            ghost.enabled = false;
            transform.rotation = Quaternion.identity;
            SetPose(Pose.Idle, blend: false);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            squashX.Step(1f, 320f, 13f, dt);
            squashY.Step(1f, 320f, 13f, dt);
            offsetX.Step(0f, 90f, 14f, dt);
            offsetY.Step(0f, 90f, 14f, dt);
            tilt.Step(0f, 160f, 12f, dt);
            pivot.localPosition = new Vector3(offsetX.Value, offsetY.Value, 0f);
            pivot.localRotation = Quaternion.Euler(0f, 0f, tilt.Value);
            pivot.localScale = new Vector3(squashX.Value, squashY.Value, 1f);

            flash = Mathf.MoveTowards(flash, 0f, dt * 5f);
            mat.SetFloat("_FlashAmount", flash);
            if (ghostLife > 0f)
            {
                ghostLife = Mathf.Max(0f, ghostLife - dt / 0.16f);
                ghost.color = new Color(1f, 1f, 1f, ghostLife * 0.8f);
                ghost.transform.localScale = Body.transform.localScale * (1f + (1f - ghostLife) * 0.12f);
                ghost.enabled = ghostLife > 0f;
            }
            if (Time.time > poseUntil && pose != Pose.Idle)
            {
                SetPose(Pose.Idle);
                return;
            }
            if (anim == null) return;
            animTime += dt;
            int frame = (int)(animTime * anim.Fps);
            frame = anim.Loop ? frame % anim.Frames.Length : Mathf.Min(frame, anim.Frames.Length - 1);
            Body.sprite = anim.Frames[frame];
        }
    }
}
