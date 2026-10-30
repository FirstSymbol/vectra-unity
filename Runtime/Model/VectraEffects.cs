using System;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    /// <summary>Base class of every effect in a shape's effect stack ([SerializeReference] polymorphic list).</summary>
    [Serializable]
    public abstract class VectraEffect
    {
        public bool enabled = true;

        public abstract string DisplayName { get; }

        public virtual VectraEffect Clone() => (VectraEffect)MemberwiseClone();

        /// <summary>Hash of the parameters; used to detect changes cheaply.</summary>
        public virtual int ComputeHash() => JsonUtility.ToJson(this).GetHashCode();
    }

    [Serializable]
    public sealed class DropShadowEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = new Color(0f, 0f, 0f, 0.35f);
        public Vector2 offset = new Vector2(0f, -4f);
        [Min(0f)] public float blur = 8f;
        public float spread;
        public LayerBlend blend = LayerBlend.Normal;

        public override string DisplayName => "Drop Shadow";
    }

    [Serializable]
    public sealed class OuterGlowEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = new Color(1f, 0.85f, 0.3f, 0.8f);
        [Min(0f)] public float size = 12f;
        public float spread;
        public LayerBlend blend = LayerBlend.Additive;

        public override string DisplayName => "Outer Glow";
    }

    [Serializable]
    public sealed class InnerShadowEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = new Color(0f, 0f, 0f, 0.5f);
        public Vector2 offset = new Vector2(0f, -2f);
        [Min(0f)] public float blur = 4f;
        [Min(0f)] public float choke;
        public LayerBlend blend = LayerBlend.Normal;

        public override string DisplayName => "Inner Shadow";
    }

    [Serializable]
    public sealed class InnerGlowEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = new Color(1f, 1f, 1f, 0.6f);
        [Min(0f)] public float size = 8f;
        [Min(0f)] public float choke;
        public LayerBlend blend = LayerBlend.Additive;

        public override string DisplayName => "Inner Glow";
    }

    [Serializable]
    public sealed class BevelEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color highlight = new Color(1f, 1f, 1f, 1f);
        [Min(0.01f)] public float size = 6f;
        [Range(0f, 360f)] public float lightAngle = 135f;
        [Range(0f, 1f)] public float intensity = 0.6f;
        [Range(0f, 1f)] public float softness;

        public override string DisplayName => "Bevel & Emboss";
    }

    [Serializable]
    public sealed class LayerBlurEffect : VectraEffect
    {
        [Min(0f)] public float blur = 4f;

        public override string DisplayName => "Layer Blur";
    }

    [Serializable]
    public sealed class GrainEffect : VectraEffect
    {
        [Range(0f, 1f)] public float amount = 0.12f;
        [Min(1f)] public float size = 1f;

        public override string DisplayName => "Noise / Grain";
    }

    [Serializable]
    public sealed class ColorAdjustEffect : VectraEffect
    {
        [Range(-180f, 180f)] public float hueShift;
        [Range(0f, 2f)] public float saturation = 1f;
        [Range(-1f, 1f)] public float brightness;
        [Range(0f, 2f)] public float contrast = 1f;

        public override string DisplayName => "Color Adjust";
    }

    public enum FillAmountMode
    {
        Horizontal,
        Vertical,
        Radial
    }

    [Serializable]
    public sealed class FillAmountEffect : VectraEffect
    {
        public FillAmountMode mode = FillAmountMode.Radial;
        [Range(0f, 1f)] public float amount = 0.75f;
        /// <summary>Horizontal: fill from the right. Vertical: fill from the top. Radial: clockwise.</summary>
        public bool reverse;
        /// <summary>Radial start angle in degrees, counter-clockwise from +X (90 = top).</summary>
        public float startAngle = 90f;

        public override string DisplayName => "Fill Amount";
    }

    [Serializable]
    public sealed class DissolveEffect : VectraEffect
    {
        [Range(0f, 1f)] public float amount = 0.3f;
        [Range(0f, 1f)] public float softness = 0.1f;
        [Min(0.01f)] public float scale = 12f;

        public override string DisplayName => "Dissolve";
    }

    /// <summary>Quantizes the outline to square cells (retro look). Applies to fills and distance-based layers.</summary>
    [Serializable]
    public sealed class PixelateEffect : VectraEffect
    {
        [Min(0.5f)] public float cellSize = 4f;

        public override string DisplayName => "Pixelate";
    }

    /// <summary>A band that follows the distance field of the shape (rounded at every corner, unlike a stroke).</summary>
    [Serializable]
    public sealed class OutlineEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = Color.white;
        [Min(0f)] public float width = 2f;
        /// <summary>Distance of the band centre from the edge; positive values move it outside.</summary>
        public float offset = 3f;
        public LayerBlend blend = LayerBlend.Normal;

        public override string DisplayName => "Outline";
    }

    /// <summary>Paints a colour over the fills (inside the shape).</summary>
    [Serializable]
    public sealed class ColorOverlayEffect : VectraEffect
    {
        [ColorUsage(true, true)] public Color color = new Color(1f, 0.2f, 0.3f, 0.5f);
        public LayerBlend blend = LayerBlend.Normal;

        [NonSerialized] VectraPaint m_Paint;

        public override string DisplayName => "Color Overlay";

        internal VectraPaint ToPaint()
        {
            m_Paint ??= new VectraPaint();
            m_Paint.type = PaintType.Solid;
            m_Paint.color = color;
            m_Paint.blend = blend;
            return m_Paint;
        }

        public override VectraEffect Clone()
        {
            var c = (ColorOverlayEffect)MemberwiseClone();
            c.m_Paint = null;
            return c;
        }
    }

    public enum OverlayGradientType
    {
        Linear,
        Radial,
        Angular,
        Diamond
    }

    /// <summary>Paints a gradient over the fills (inside the shape).</summary>
    [Serializable]
    public sealed class GradientOverlayEffect : VectraEffect
    {
        public VectraGradient gradient = VectraGradient.Create(new Color(1f, 1f, 1f, 0.6f), new Color(1f, 1f, 1f, 0f));
        public OverlayGradientType type = OverlayGradientType.Linear;
        /// <summary>Linear: direction in degrees (90 = bottom to top). Other types: rotation.</summary>
        public float angle = 90f;
        [Min(0.01f)] public float scale = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        public LayerBlend blend = LayerBlend.Normal;

        [NonSerialized] VectraPaint m_Paint;

        public override string DisplayName => "Gradient Overlay";

        internal VectraPaint ToPaint()
        {
            m_Paint ??= new VectraPaint();
            m_Paint.gradient = gradient;
            m_Paint.opacity = opacity;
            m_Paint.blend = blend;
            float r = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            switch (type)
            {
                case OverlayGradientType.Linear:
                    m_Paint.type = PaintType.LinearGradient;
                    m_Paint.gradientStart = new Vector2(0.5f, 0.5f) - dir * (0.5f * scale);
                    m_Paint.gradientEnd = new Vector2(0.5f, 0.5f) + dir * (0.5f * scale);
                    break;
                default:
                    m_Paint.type = type == OverlayGradientType.Radial ? PaintType.RadialGradient :
                        type == OverlayGradientType.Angular ? PaintType.AngularGradient : PaintType.DiamondGradient;
                    m_Paint.gradientStart = new Vector2(0.5f, 0.5f);
                    m_Paint.gradientEnd = new Vector2(0.5f, 0.5f) + dir * (0.5f * scale);
                    break;
            }

            return m_Paint;
        }

        public override VectraEffect Clone()
        {
            var c = (GradientOverlayEffect)MemberwiseClone();
            c.gradient = gradient?.Clone();
            c.m_Paint = null;
            return c;
        }
    }
}
