using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>
    /// Exposes the most common shape parameters as plain fields so the Animation window, Animator and Timeline can
    /// key them (layer lists and polymorphic effects cannot be keyed directly). Only the parameters whose "drive"
    /// toggle is on are written to the <see cref="VectraShape"/> or <see cref="VectraRenderer"/> on this GameObject.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Vectra/Vectra Animated Properties")]
    public class VectraAnimatedProperties : MonoBehaviour
    {
        [Min(0)] public int fillIndex;
        public bool driveFillColor;
        [ColorUsage(true, true)] public Color fillColor = Color.white;
        public bool driveFillOpacity;
        [Range(0f, 1f)] public float fillOpacity = 1f;

        [Min(0)] public int strokeIndex;
        public bool driveStrokeColor;
        [ColorUsage(true, true)] public Color strokeColor = Color.black;
        public bool driveStrokeWidth;
        [Min(0f)] public float strokeWidth = 2f;
        public bool driveTrim;
        [Range(0f, 1f)] public float trimStart;
        [Range(0f, 1f)] public float trimEnd = 1f;
        public float trimOffset;
        public bool driveDashOffset;
        public float dashOffset;

        public bool driveRotation;
        public float rotation;
        public bool driveFillAmount;
        [Range(0f, 1f)] public float fillAmount = 1f;
        public bool driveDissolve;
        [Range(0f, 1f)] public float dissolve;
        public bool driveMorph;
        [Range(0f, 1f)] public float morph = 0.5f;

        IVectraShape m_Shape;
        int m_LastHash;

        IVectraShape Shape
        {
            get
            {
                if (m_Shape != null && (Object)m_Shape != null) return m_Shape;
                m_Shape = GetComponent<IVectraShape>();
                return m_Shape;
            }
        }

        void OnEnable() => Apply(true);

        // Called by the animation system after it wrote the fields.
        void OnDidApplyAnimationProperties() => Apply(false);

        void LateUpdate() => Apply(false);

        void OnValidate() => Apply(true);

        /// <summary>Copies the current values of the shape into the fields (does not change the drive toggles).</summary>
        public void Capture()
        {
            var s = Shape;
            if (s == null) return;
            var c = s.Content;
            if (fillIndex < c.ActiveFills.Count && c.ActiveFills[fillIndex]?.paint != null)
            {
                fillColor = c.ActiveFills[fillIndex].paint.color;
                fillOpacity = c.ActiveFills[fillIndex].paint.opacity;
            }

            if (strokeIndex < c.ActiveStrokes.Count && c.ActiveStrokes[strokeIndex] != null)
            {
                var st = c.ActiveStrokes[strokeIndex];
                strokeColor = st.paint != null ? st.paint.color : strokeColor;
                strokeWidth = st.width;
                trimStart = st.trimStart;
                trimEnd = st.trimEnd;
                trimOffset = st.trimOffset;
                dashOffset = st.dashOffset;
            }

            if (c.geometry != null) rotation = c.geometry.rotation;
            if (c.geometry is MorphShape m) morph = m.t;
            foreach (var e in c.ActiveEffects)
            {
                if (e is FillAmountEffect fa) fillAmount = fa.amount;
                if (e is DissolveEffect d) dissolve = d.amount;
            }
        }

        int ComputeHash()
        {
            var h = new HashBuilder(fillIndex);
            h.Add(strokeIndex);
            if (driveFillColor) h.Add(fillColor.GetHashCode());
            if (driveFillOpacity) h.Add(fillOpacity);
            if (driveStrokeColor) h.Add(strokeColor.GetHashCode());
            if (driveStrokeWidth) h.Add(strokeWidth);
            if (driveTrim)
            {
                h.Add(trimStart);
                h.Add(trimEnd);
                h.Add(trimOffset);
            }

            if (driveDashOffset) h.Add(dashOffset);
            if (driveRotation) h.Add(rotation);
            if (driveFillAmount) h.Add(fillAmount);
            if (driveDissolve) h.Add(dissolve);
            if (driveMorph) h.Add(morph);
            return h.Value;
        }

        void Apply(bool force)
        {
            var s = Shape;
            if (s == null || !isActiveAndEnabled) return;
            int hash = ComputeHash();
            if (!force && hash == m_LastHash) return;
            m_LastHash = hash;

            var c = s.Content;
            if (fillIndex < c.ActiveFills.Count && c.ActiveFills[fillIndex]?.paint != null)
            {
                var p = c.ActiveFills[fillIndex].paint;
                if (driveFillColor) p.color = fillColor;
                if (driveFillOpacity) p.opacity = fillOpacity;
            }

            if (strokeIndex < c.ActiveStrokes.Count && c.ActiveStrokes[strokeIndex] != null)
            {
                var st = c.ActiveStrokes[strokeIndex];
                if (driveStrokeColor && st.paint != null) st.paint.color = strokeColor;
                if (driveStrokeWidth) st.width = strokeWidth;
                if (driveTrim)
                {
                    st.trimStart = trimStart;
                    st.trimEnd = trimEnd;
                    st.trimOffset = trimOffset;
                }

                if (driveDashOffset) st.dashOffset = dashOffset;
            }

            if (driveRotation && c.geometry != null) c.geometry.rotation = rotation;
            if (driveMorph && c.geometry is MorphShape m) m.t = morph;
            if (driveFillAmount) Effect<FillAmountEffect>(c).amount = fillAmount;
            if (driveDissolve) Effect<DissolveEffect>(c).amount = dissolve;
            s.SetShapeDirty();
        }

        static T Effect<T>(VectraShapeContent c) where T : VectraEffect, new()
        {
            foreach (var e in c.ActiveEffects)
                if (e is T t) return t;
            var n = new T();
            c.ActiveEffects.Add(n);
            return n;
        }

        void Reset() => Capture();
    }
}
