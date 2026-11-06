using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lucky
{
    /// <summary>Post-processing punches layered on top of the global volume's base look.</summary>
    public class PostFX : MonoBehaviour
    {
        public static PostFX I { get; private set; }

        Bloom bloom;
        ChromaticAberration chroma;
        LensDistortion lens;
        ColorAdjustments color;
        Vignette vignette;

        float baseBloom, baseVignette, baseSaturation;
        Spring bloomPunch, chromaPunch, lensPunch, satPunch, exposurePunch, vignettePunch;
        Color vignetteBase, vignetteTint;
        float tintAmount, danger;

        public void Init(Volume volume)
        {
            I = this;
            var profile = volume.profile; // runtime instance, the asset stays untouched
            profile.TryGet(out bloom);
            profile.TryGet(out chroma);
            profile.TryGet(out lens);
            profile.TryGet(out color);
            profile.TryGet(out vignette);
            if (bloom) baseBloom = bloom.intensity.value;
            if (vignette) { baseVignette = vignette.intensity.value; vignetteBase = vignette.color.value; }
            if (color) baseSaturation = color.saturation.value;
        }

        public void Punch(float bloomAdd = 0f, float chromaAdd = 0f, float lensAdd = 0f, float saturationAdd = 0f, float exposureAdd = 0f)
        {
            float flash = SaveData.Flashes ? 1f : 0.35f;
            bloomPunch.Value += bloomAdd * flash;
            chromaPunch.Value += chromaAdd;
            lensPunch.Value += lensAdd;
            satPunch.Value += saturationAdd;
            exposurePunch.Value += exposureAdd * flash;
        }

        public void VignettePulse(Color tint, float amount)
        {
            vignetteTint = tint;
            tintAmount = 1f;
            vignettePunch.Value += amount;
        }

        /// <summary>0..1 persistent low-health danger vignette.</summary>
        public void SetDanger(float value) => danger = value;

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            bloomPunch.Step(0f, 40f, 9f, dt);
            chromaPunch.Step(0f, 50f, 10f, dt);
            lensPunch.Step(0f, 120f, 9f, dt);
            satPunch.Step(0f, 30f, 8f, dt);
            exposurePunch.Step(0f, 60f, 11f, dt);
            vignettePunch.Step(0f, 20f, 7f, dt);
            tintAmount = Mathf.MoveTowards(tintAmount, 0f, dt * 1.2f);

            float pulse = danger * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f));
            if (bloom) bloom.intensity.value = Mathf.Max(0f, baseBloom + bloomPunch.Value);
            if (chroma) chroma.intensity.value = Mathf.Clamp01(0.05f + chromaPunch.Value + danger * 0.12f);
            if (lens) lens.intensity.value = Mathf.Clamp(lensPunch.Value, -0.6f, 0.6f);
            if (color)
            {
                color.saturation.value = Mathf.Clamp(baseSaturation + satPunch.Value, -100f, 100f);
                color.postExposure.value = exposurePunch.Value;
            }
            if (vignette)
            {
                var danger01 = Mathf.Max(tintAmount, danger * 0.8f);
                var tint = danger > tintAmount ? new Color(0.55f, 0f, 0.05f) : vignetteTint;
                vignette.color.value = Color.Lerp(vignetteBase, tint, danger01);
                vignette.intensity.value = Mathf.Clamp01(baseVignette + vignettePunch.Value + danger * 0.12f + pulse * 0.08f);
            }
        }
    }
}
