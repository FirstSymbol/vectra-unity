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
}
