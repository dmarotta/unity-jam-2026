using System;
using System.Collections;
using UnityEngine;

namespace Lucky
{
    /// <summary>Coroutine tweens. Scaled tweens freeze during hitstop; unscaled ones (UI) keep running.</summary>
    public static class Tween
    {
        public static IEnumerator Run(float duration, Action<float> step, Func<float, float> ease = null, bool unscaled = false)
        {
            ease ??= Ease.OutCubic;
            float t = 0f;
            while (t < duration)
            {
                step(ease(t / duration));
                yield return null;
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            }
            step(ease(1f));
        }

        public static IEnumerator Wait(float seconds, bool unscaled = false)
        {
            if (unscaled) yield return new WaitForSecondsRealtime(seconds);
            else yield return new WaitForSeconds(seconds);
        }
    }

    /// <summary>Critically-damped-ish spring used for squash, kicks and bounces.</summary>
    public struct Spring
    {
        public float Value, Velocity;

        public void Step(float target, float stiffness, float damping, float dt)
        {
            float force = (target - Value) * stiffness - Velocity * damping;
            Velocity += force * dt;
            Value += Velocity * dt;
        }
    }
}
