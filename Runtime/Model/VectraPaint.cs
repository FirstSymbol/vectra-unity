using System;
using System.Collections.Generic;
using FirstSymbol.Vectra.Geometry;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    public enum PaintType
    {
        Solid,
        LinearGradient,
        RadialGradient,
        AngularGradient,
        DiamondGradient,
        Texture,
        Stripes,
        Checker,
        Dots,
        Grid,
        Noise,
        Freeform,
        /// <summary>Gradient along the distance from the outline (follows the shape: edge fades, rings, contour bands).</summary>
        ContourGradient
    }

    public enum SpreadMode
    {
        Pad,
        Repeat,
        Reflect
    }

    public enum TextureFit
    {
        Stretch,
        Fit,
        Fill,
        Tile
    }

    /// <summary>How a layer is composited over what is below it inside the same shape.</summary>
    public enum LayerBlend
    {
        Normal,
        Additive
    }

    [Serializable]
    public struct FreeformPoint
    {
        /// <summary>Position in normalized shape bounds (0..1).</summary>
        public Vector2 position;
        [ColorUsage(true, true)] public Color color;
        /// <summary>Influence radius exponent; higher values make sharper colour regions.</summary>
        [Range(0.5f, 8f)] public float falloff;

        public FreeformPoint(Vector2 position, Color color, float falloff = 2f)
        {
            this.position = position;
            this.color = color;
            this.falloff = falloff;
        }
    }

    /// <summary>
    /// What a fill or stroke is painted with: a colour, a gradient, a texture, a procedural pattern or a freeform
    /// (mesh-like) gradient. Gradient handles are expressed in normalized shape bounds (0..1), so they follow the
    /// shape when it is resized.
    /// </summary>
    [Serializable]
    public class VectraPaint
    {
        public PaintType type = PaintType.Solid;
        [ColorUsage(true, true)] public Color color = Color.white;
        [Range(0f, 1f)] public float opacity = 1f;
        public LayerBlend blend = LayerBlend.Normal;
        /// <summary>Name of a colour in the active <see cref="VectraPalette"/>; overrides <see cref="color"/> when found.</summary>
        public string colorToken;
        /// <summary>Palette colour for the secondary pattern colour.</summary>
        public string secondaryColorToken;

        // Gradients
        public VectraGradient gradient = VectraGradient.Default();
        public Vector2 gradientStart = new Vector2(0f, 0.5f);
        public Vector2 gradientEnd = new Vector2(1f, 0.5f);
        /// <summary>Radial/angular/diamond gradients: ratio of the secondary axis to the primary one.</summary>
        public float gradientAspect = 1f;
        /// <summary>Radial gradients: focal point inside the unit circle (-1..1). Zero is a centred gradient.</summary>
        public Vector2 focalPoint = Vector2.zero;
        public SpreadMode spread = SpreadMode.Pad;
        /// <summary>Use <see cref="customMapping"/> (local position to gradient space) instead of the handles (used by SVG).</summary>
        public bool useCustomMapping;
        public Geometry.Affine2 customMapping = Geometry.Affine2.Identity;

        /// <summary>Contour gradients: distance from the outline (local units) that spans the whole gradient.</summary>
        [Min(0.01f)] public float contourDepth = 24f;

        // Texture
        public Texture texture;
        public Rect textureUvRect = new Rect(0f, 0f, 1f, 1f);
        public TextureFit textureFit = TextureFit.Stretch;
        public Vector2 textureScale = Vector2.one;
        public Vector2 textureOffset = Vector2.zero;
        public float textureRotation;

        // Patterns
        [ColorUsage(true, true)] public Color secondaryColor = new Color(0f, 0f, 0f, 1f);
        public float patternSize = 16f;
        public float patternRotation;
        [Range(0f, 1f)] public float patternRatio = 0.5f;
        public Vector2 patternOffset = Vector2.zero;

        // Freeform
        public List<FreeformPoint> freeformPoints = new List<FreeformPoint>();

        public static VectraPaint Solid(Color c) => new VectraPaint { type = PaintType.Solid, color = c };

        public static VectraPaint Linear(Color a, Color b, float angleDegrees = 0f)
        {
            var p = new VectraPaint { type = PaintType.LinearGradient, gradient = VectraGradient.Create(a, b) };
            p.SetLinearAngle(angleDegrees);
            return p;
        }

        /// <summary>Gradient from the edge (t = 0) to <paramref name="depth"/> units inside (t = 1).</summary>
        public static VectraPaint Contour(Color edge, Color inner, float depth) => new VectraPaint
        {
            type = PaintType.ContourGradient, gradient = VectraGradient.Create(edge, inner), contourDepth = depth
        };

        public static VectraPaint Radial(Color inner, Color outer) => new VectraPaint
        {
            type = PaintType.RadialGradient,
            gradient = VectraGradient.Create(inner, outer),
            gradientStart = new Vector2(0.5f, 0.5f),
            gradientEnd = new Vector2(1f, 0.5f)
        };

        /// <summary>Places the linear gradient handles across the bounds at the given angle (0 = left to right).</summary>
        public void SetLinearAngle(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 0.5f;
            gradientStart = new Vector2(0.5f, 0.5f) - d;
            gradientEnd = new Vector2(0.5f, 0.5f) + d;
        }

        /// <summary><see cref="color"/>, or the palette colour named by <see cref="colorToken"/>.</summary>
        public Color ResolvedColor => string.IsNullOrEmpty(colorToken) ? color : VectraPalette.Resolve(colorToken, color);

        public Color ResolvedSecondaryColor =>
            string.IsNullOrEmpty(secondaryColorToken) ? secondaryColor : VectraPalette.Resolve(secondaryColorToken, secondaryColor);

        public bool IsGradient => (type >= PaintType.LinearGradient && type <= PaintType.DiamondGradient) || type == PaintType.ContourGradient;

        /// <summary>The paint needs the distance to the outline (path shapes then draw through a distance field).</summary>
        public bool UsesDistance => type == PaintType.ContourGradient;
        public bool UsesGradientRow => IsGradient || (type >= PaintType.Stripes && type <= PaintType.Noise);
        public bool IsVisible => opacity > 0f && (type != PaintType.Solid || ResolvedColor.a > 0f);

        public VectraPaint Clone()
        {
            var p = (VectraPaint)MemberwiseClone();
            p.gradient = gradient?.Clone();
            p.freeformPoints = freeformPoints != null ? new List<FreeformPoint>(freeformPoints) : new List<FreeformPoint>();
            return p;
        }

        /// <summary>Hash of everything that is baked into atlas rows (gradient stops, pattern colours, freeform).</summary>
        public int ComputeRowHash(float hueShift = 0, float saturation = 1, float brightness = 0, float contrast = 1)
        {
            unchecked
            {
                int h = (int)type * 1000003;
                if (IsGradient) h = h * 31 + (gradient?.ComputeHash() ?? 0);
                else if (type >= PaintType.Stripes && type <= PaintType.Noise)
                {
                    h = h * 31 + ResolvedColor.GetHashCode();
                    h = h * 31 + ResolvedSecondaryColor.GetHashCode();
                }
                else if (type == PaintType.Freeform && freeformPoints != null)
                {
                    foreach (var fp in freeformPoints)
                    {
                        h = h * 31 + fp.position.GetHashCode();
                        h = h * 31 + fp.color.GetHashCode();
                        h = h * 31 + fp.falloff.GetHashCode();
                    }
                }

                h = h * 31 + hueShift.GetHashCode();
                h = h * 31 + saturation.GetHashCode();
                h = h * 31 + brightness.GetHashCode();
                h = h * 31 + contrast.GetHashCode();
                return h;
            }
        }
    }

    public enum StrokeAlignment
    {
        Center,
        Inside,
        Outside
    }

    [Serializable]
    public class FillLayer
    {
        public bool enabled = true;
        public VectraPaint paint = new VectraPaint();

        public FillLayer() { }
        public FillLayer(VectraPaint paint) => this.paint = paint;
        public FillLayer Clone() => new FillLayer { enabled = enabled, paint = paint?.Clone() };
    }

    [Serializable]
    public class StrokeLayer
    {
        public bool enabled = true;
        public VectraPaint paint = VectraPaint.Solid(Color.black);
        [Min(0f)] public float width = 2f;
        public StrokeAlignment alignment = StrokeAlignment.Center;
        public LineCap cap = LineCap.Butt;
        public LineJoin join = LineJoin.Miter;
        [Min(1f)] public float miterLimit = 4f;

        public bool dashed;
        public List<float> dashPattern = new List<float> { 6f, 4f };
        public float dashOffset;
        /// <summary>When set, dash lengths are fractions of the contour length (0..1).</summary>
        public bool dashRelative;

        [Range(0f, 1f)] public float trimStart;
        [Range(0f, 1f)] public float trimEnd = 1f;
        public float trimOffset;

        public bool useWidthProfile;
        public AnimationCurve widthProfile = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        /// <summary>Draws the stroke below the fills instead of above them.</summary>
        public bool behindFill;

        public StrokeLayer() { }

        public StrokeLayer(Color color, float width)
        {
            paint = VectraPaint.Solid(color);
            this.width = width;
        }

        public bool HasTrim => trimStart > 0f || trimEnd < 1f;

        /// <summary>True when the stroke needs real geometry (cannot be an analytic band of the distance field).</summary>
        public bool NeedsGeometry => dashed || HasTrim || useWidthProfile;

        public StrokeLayer Clone()
        {
            var s = (StrokeLayer)MemberwiseClone();
            s.paint = paint?.Clone();
            s.dashPattern = dashPattern != null ? new List<float>(dashPattern) : new List<float>();
            s.widthProfile = widthProfile != null ? new AnimationCurve(widthProfile.keys) : null;
            return s;
        }

        public int ComputeGeometryHash()
        {
            unchecked
            {
                int h = width.GetHashCode();
                h = h * 31 + (int)alignment;
                h = h * 31 + (int)cap;
                h = h * 31 + (int)join;
                h = h * 31 + miterLimit.GetHashCode();
                h = h * 31 + (dashed ? 1 : 0);
                if (dashed && dashPattern != null)
                    foreach (var d in dashPattern) h = h * 31 + d.GetHashCode();
                h = h * 31 + dashOffset.GetHashCode();
                h = h * 31 + (dashRelative ? 1 : 0);
                h = h * 31 + trimStart.GetHashCode();
                h = h * 31 + trimEnd.GetHashCode();
                h = h * 31 + trimOffset.GetHashCode();
                h = h * 31 + (useWidthProfile ? 1 : 0);
                if (useWidthProfile && widthProfile != null)
                    foreach (var k in widthProfile.keys) h = h * 31 + k.value.GetHashCode() + k.time.GetHashCode() * 7;
                return h;
            }
        }
    }
}
