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

            transform.position = basePos + new Vector3(ox + kickX.Value, oy + kickY.Value, 0f);
            transform.rotation = Quaternion.Euler(0f, 0f, roll);
            cam.orthographicSize = BaseSize * (1f + zoom.Value);
        }
    }
}
