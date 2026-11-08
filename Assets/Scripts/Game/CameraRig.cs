using System.Collections;
using UnityEngine;

namespace Lucky
{
    /// <summary>Trauma shake, directional kicks and zoom punches. Runs on unscaled time so hitstop still shakes.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }
        public const float BaseSize = 5.4f;

        Camera cam;
        Vector3 basePos;
        float trauma;
        Spring kickX, kickY, zoom;
        float seed;
        Vector2 focusOffset;
        float focusZoom;
        Coroutine focusing;

        public Camera Cam => cam;

        void Awake()
        {
            I = this;
            cam = GetComponent<Camera>();
            basePos = transform.position;
            seed = Random.value * 100f;
        }

        public void Shake(float amount)
        {
            float scale = SaveData.ScreenShake ? 1f : 0.15f;
            trauma = Mathf.Clamp01(trauma + amount * scale);
        }

        public void Kick(Vector2 direction, float amount)
        {
            float scale = SaveData.ScreenShake ? 1f : 0.2f;
            kickX.Velocity += direction.x * amount * 60f * scale;
            kickY.Velocity += direction.y * amount * 60f * scale;
        }

        /// <summary>Negative zooms in.</summary>
        public void ZoomPunch(float amount) => zoom.Velocity += amount * 40f;

        public float BaseX => basePos.x;

        public IEnumerator PanTo(float x, float duration)
        {
            float start = basePos.x;
            yield return Tween.Run(duration, t => basePos.x = Mathf.LerpUnclamped(start, x, t), Ease.InOutCubic);
        }

        public void SnapTo(float x) => basePos.x = x;

        /// <summary>
        /// Cinematic framing held on top of the base view: glide to <paramref name="offset"/> and <paramref name="zoom"/>
        /// (negative zooms in) over real time; Focus(zero, 0) releases it. Keep within the arena's ~1.5 unit margin.
        /// </summary>
        public void Focus(Vector2 offset, float zoom, float duration)
        {
            if (focusing != null) StopCoroutine(focusing);
            Vector2 o0 = focusOffset;
            float z0 = focusZoom;
            focusing = StartCoroutine(Tween.Run(duration, t =>
            {
                focusOffset = Vector2.LerpUnclamped(o0, offset, t);
                focusZoom = Mathf.LerpUnclamped(z0, zoom, t);
            }, Ease.InOutCubic, true));
        }

        public void ClearFocus()
        {
            if (focusing != null) StopCoroutine(focusing);
            focusOffset = Vector2.zero;
            focusZoom = 0f;
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            trauma = Mathf.Max(0f, trauma - dt * 1.4f);
            kickX.Step(0f, 260f, 18f, dt);
            kickY.Step(0f, 260f, 18f, dt);
            zoom.Step(0f, 180f, 14f, dt);

            float t = Time.unscaledTime * 22f;
            float shake = trauma * trauma;
            float ox = (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f * 0.6f * shake;
            float oy = (Mathf.PerlinNoise(seed + 10f, t) - 0.5f) * 2f * 0.45f * shake;
            float roll = (Mathf.PerlinNoise(seed + 20f, t) - 0.5f) * 2f * 2.2f * shake;

            transform.position = basePos + new Vector3(ox + kickX.Value + focusOffset.x, oy + kickY.Value + focusOffset.y, 0f);
            transform.rotation = Quaternion.Euler(0f, 0f, roll);
            cam.orthographicSize = BaseSize * (1f + zoom.Value + focusZoom);
        }
    }
}
