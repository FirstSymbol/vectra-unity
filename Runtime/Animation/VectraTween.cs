using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FirstSymbol.Vectra
{
    /// <summary>Common surface of <see cref="VectraShape"/> (Canvas) and <see cref="VectraRenderer"/> (world).</summary>
    public interface IVectraShape
    {
        VectraShapeContent Content { get; }

        /// <summary>Call after changing geometry, fills, strokes or effects from code.</summary>
        void SetShapeDirty();
    }

    public enum VectraEase
    {
        Linear,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        InOutSine,
        OutBack,
        OutElastic,
        OutBounce
    }

    /// <summary>
    /// Minimal dependency-free tween. Created by the <see cref="VectraTweens"/> extension methods, e.g.
    /// <c>shape.TweenTrim(0f, 1f, 0.8f).SetEase(VectraEase.OutCubic).SetLoops(-1, true)</c>.
    /// Tweens are updated once per frame and stop by themselves when their target is destroyed.
    /// </summary>
    public sealed class VectraTween
    {
        Action<float> m_Apply;
        Object m_Target;
        bool m_HasTarget;
        float m_Duration, m_Delay, m_Time;
        int m_Loops = 1, m_Done;
        bool m_Yoyo, m_Unscaled, m_Killed;
        VectraEase m_Ease = VectraEase.OutQuad;
        Action m_OnComplete;

        static readonly List<VectraTween> s_Active = new List<VectraTween>();
        static readonly List<VectraTween> s_Pending = new List<VectraTween>();
        static bool s_Hooked;

        public Object Target => m_Target;
        public bool IsActive => !m_Killed;

        /// <summary>Starts a tween that calls <paramref name="apply"/> with eased progress 0..1.</summary>
        public static VectraTween Create(Object target, float duration, Action<float> apply)
        {
            var t = new VectraTween { m_Target = target, m_HasTarget = target != null, m_Duration = Mathf.Max(duration, 0f), m_Apply = apply };
            s_Pending.Add(t);
            EnsureDriver();
            return t;
        }

        public static VectraTween Float(Object target, float from, float to, float duration, Action<float> setter) =>
            Create(target, duration, k => setter(Mathf.LerpUnclamped(from, to, k)));

        public static VectraTween Color(Object target, Color from, Color to, float duration, Action<Color> setter) =>
            Create(target, duration, k => setter(UnityEngine.Color.LerpUnclamped(from, to, k)));

        public VectraTween SetEase(VectraEase ease)
        {
            m_Ease = ease;
            return this;
        }

        public VectraTween SetDelay(float seconds)
        {
            m_Delay = Mathf.Max(seconds, 0f);
            return this;
        }

        /// <summary>Number of plays (-1 = forever). With <paramref name="yoyo"/> every other play runs backwards.</summary>
        public VectraTween SetLoops(int loops, bool yoyo = false)
        {
            m_Loops = loops == 0 ? 1 : loops;
            m_Yoyo = yoyo;
            return this;
        }

        public VectraTween SetUnscaledTime(bool unscaled = true)
        {
            m_Unscaled = unscaled;
            return this;
        }

        public VectraTween OnComplete(Action callback)
        {
            m_OnComplete += callback;
            return this;
        }

        /// <summary>Stops the tween; with <paramref name="complete"/> it first jumps to the end value.</summary>
        public void Kill(bool complete = false)
        {
            if (m_Killed) return;
            if (complete && m_Target != null) m_Apply(Evaluate(m_Ease, m_Yoyo && m_Loops > 0 && (m_Loops & 1) == 0 ? 0f : 1f));
            m_Killed = true;
        }

        /// <summary>Stops every tween of <paramref name="target"/>.</summary>
        public static void KillAll(Object target, bool complete = false)
        {
            foreach (var list in new[] { s_Active, s_Pending })
                foreach (var t in list)
                    if (t.m_Target == target) t.Kill(complete);
        }

        public static int ActiveCount => s_Active.Count + s_Pending.Count;

        static void EnsureDriver()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (s_Hooked) return;
                s_Hooked = true;
                UnityEditor.EditorApplication.update += () =>
                {
                    if (!Application.isPlaying) Step(Time.realtimeSinceStartup, Time.realtimeSinceStartup);
                };
                return;
            }
#endif
            VectraTweenDriver.Ensure();
        }

        static float s_LastScaled = -1f, s_LastUnscaled = -1f;

        internal static void Step(float scaledNow, float unscaledNow)
        {
            float dt = s_LastScaled < 0f ? 0f : Mathf.Clamp(scaledNow - s_LastScaled, 0f, 0.25f);
            float udt = s_LastUnscaled < 0f ? 0f : Mathf.Clamp(unscaledNow - s_LastUnscaled, 0f, 0.25f);
            s_LastScaled = scaledNow;
            s_LastUnscaled = unscaledNow;
            if (s_Pending.Count > 0)
            {
                s_Active.AddRange(s_Pending);
                s_Pending.Clear();
            }

            for (int i = s_Active.Count - 1; i >= 0; i--)
            {
                var t = s_Active[i];
                if (t.m_Killed || (t.m_HasTarget && t.m_Target == null) || !t.Advance(t.m_Unscaled ? udt : dt))
                {
                    s_Active.RemoveAt(i);
                    if (!t.m_Killed && (!t.m_HasTarget || t.m_Target != null))
                    {
                        t.m_Killed = true;
                        t.m_OnComplete?.Invoke();
                    }
                }
            }
        }

        /// <summary>Returns false when the tween has finished.</summary>
        bool Advance(float dt)
        {
            if (m_Delay > 0f)
            {
                m_Delay -= dt;
                if (m_Delay > 0f) return true;
                dt = -m_Delay;
                m_Delay = 0f;
            }

            m_Time += dt;
            if (m_Duration <= 0f)
            {
                m_Apply(Evaluate(m_Ease, 1f));
                return false;
            }

            while (m_Time >= m_Duration)
            {
                m_Time -= m_Duration;
                m_Done++;
                if (m_Loops > 0 && m_Done >= m_Loops)
                {
                    bool backwards = m_Yoyo && (m_Done & 1) == 0;
                    m_Apply(Evaluate(m_Ease, backwards ? 0f : 1f));
                    return false;
                }
            }

            float k = m_Time / m_Duration;
            if (m_Yoyo && (m_Done & 1) == 1) k = 1f - k;
            m_Apply(Evaluate(m_Ease, k));
            return true;
        }

        public static float Evaluate(VectraEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case VectraEase.InQuad: return t * t;
                case VectraEase.OutQuad: return 1f - (1f - t) * (1f - t);
                case VectraEase.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
                case VectraEase.InCubic: return t * t * t;
                case VectraEase.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case VectraEase.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case VectraEase.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case VectraEase.OutBack:
                {
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                }
                case VectraEase.OutElastic:
                    return t <= 0f ? 0f : t >= 1f ? 1f : Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * (2f * Mathf.PI / 3f)) + 1f;
                case VectraEase.OutBounce:
                {
                    const float n1 = 7.5625f, d1 = 2.75f;
                    if (t < 1f / d1) return n1 * t * t;
                    if (t < 2f / d1) return n1 * (t -= 1.5f / d1) * t + 0.75f;
                    if (t < 2.5f / d1) return n1 * (t -= 2.25f / d1) * t + 0.9375f;
                    return n1 * (t -= 2.625f / d1) * t + 0.984375f;
                }
                default: return t;
            }
        }
    }

    [AddComponentMenu("")]
    sealed class VectraTweenDriver : MonoBehaviour
    {
        static VectraTweenDriver s_Instance;

        internal static void Ensure()
        {
            if (s_Instance != null) return;
            var go = new GameObject("Vectra Tweens") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<VectraTweenDriver>();
        }

        void Update() => VectraTween.Step(Time.time, Time.unscaledTime);
    }

    /// <summary>Fluent tweens for the most common animated shape parameters.</summary>
    public static class VectraTweens
    {
        static Object AsObject(IVectraShape s) => s as Object;

        /// <summary>Generic float tween on a shape: <paramref name="setter"/> receives the value; the shape is marked dirty.</summary>
        public static VectraTween TweenFloat(this IVectraShape s, float from, float to, float duration, Action<float> setter) =>
            VectraTween.Float(AsObject(s), from, to, duration, v =>
            {
                setter(v);
                s.SetShapeDirty();
            });

        public static VectraTween TweenFillColor(this IVectraShape s, Color to, float duration, int fill = 0)
        {
            var paint = s.Content.ActiveFills[fill].paint;
            return VectraTween.Color(AsObject(s), paint.color, to, duration, c =>
            {
                paint.color = c;
                s.SetShapeDirty();
            });
        }

        public static VectraTween TweenFillOpacity(this IVectraShape s, float to, float duration, int fill = 0)
        {
            var paint = s.Content.ActiveFills[fill].paint;
            return s.TweenFloat(paint.opacity, to, duration, v => paint.opacity = v);
        }

        public static VectraTween TweenStrokeColor(this IVectraShape s, Color to, float duration, int stroke = 0)
        {
            var paint = s.Content.ActiveStrokes[stroke].paint;
            return VectraTween.Color(AsObject(s), paint.color, to, duration, c =>
            {
                paint.color = c;
                s.SetShapeDirty();
            });
        }

        public static VectraTween TweenStrokeWidth(this IVectraShape s, float to, float duration, int stroke = 0)
        {
            var layer = s.Content.ActiveStrokes[stroke];
            return s.TweenFloat(layer.width, to, duration, v => layer.width = v);
        }

        /// <summary>Animates the trim range of a stroke ("drawing" the outline): from the current range to the given one.</summary>
        public static VectraTween TweenTrim(this IVectraShape s, float start, float end, float duration, int stroke = 0)
        {
            var layer = s.Content.ActiveStrokes[stroke];
            float s0 = layer.trimStart, e0 = layer.trimEnd;
            return VectraTween.Create(AsObject(s), duration, k =>
            {
                layer.trimStart = Mathf.Clamp01(Mathf.LerpUnclamped(s0, start, k));
                layer.trimEnd = Mathf.Clamp01(Mathf.LerpUnclamped(e0, end, k));
                s.SetShapeDirty();
            });
        }

        public static VectraTween TweenDashOffset(this IVectraShape s, float to, float duration, int stroke = 0)
        {
            var layer = s.Content.ActiveStrokes[stroke];
            return s.TweenFloat(layer.dashOffset, to, duration, v => layer.dashOffset = v);
        }

        /// <summary>Rotates the geometry inside its rect (degrees).</summary>
        public static VectraTween TweenRotation(this IVectraShape s, float degrees, float duration)
        {
            var g = s.Content.geometry;
            return s.TweenFloat(g.rotation, degrees, duration, v => g.rotation = v);
        }

        /// <summary>Animates (and adds when missing) a <see cref="FillAmountEffect"/>.</summary>
        public static VectraTween TweenFillAmount(this IVectraShape s, float to, float duration) =>
            TweenEffect<FillAmountEffect>(s, e => e.amount, (e, v) => e.amount = v, to, duration);

        /// <summary>Animates (and adds when missing) a <see cref="DissolveEffect"/>.</summary>
        public static VectraTween TweenDissolve(this IVectraShape s, float to, float duration) =>
            TweenEffect<DissolveEffect>(s, e => e.amount, (e, v) => e.amount = v, to, duration);

        /// <summary>Animates the blend factor of a <see cref="MorphShape"/>.</summary>
        public static VectraTween TweenMorph(this IVectraShape s, float to, float duration)
        {
            if (!(s.Content.geometry is MorphShape m)) throw new InvalidOperationException("The shape geometry is not a MorphShape.");
            return s.TweenFloat(m.t, to, duration, v => m.t = v);
        }

        /// <summary>Animates one float of an effect, adding the effect when the shape has none of that type.</summary>
        public static VectraTween TweenEffect<T>(this IVectraShape s, Func<T, float> get, Action<T, float> set, float to, float duration)
            where T : VectraEffect, new()
        {
            T effect = null;
            foreach (var e in s.Content.ActiveEffects)
                if (e is T t)
                {
                    effect = t;
                    break;
                }

            if (effect == null)
            {
                effect = new T();
                s.Content.ActiveEffects.Add(effect);
            }

            return s.TweenFloat(get(effect), to, duration, v => set(effect, v));
        }

        /// <summary>Stops every tween running on this shape.</summary>
        public static void KillTweens(this IVectraShape s, bool complete = false) => VectraTween.KillAll(AsObject(s), complete);
    }
}
