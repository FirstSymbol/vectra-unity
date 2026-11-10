using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using FirstSymbol.Vectra.Geometry;

namespace FirstSymbol.Vectra.Svg
{
    // Figma writes angular and diamond gradients twice: a fallback in a data-figma-skip-parse group and the real shape with
    // data-figma-gradient-fill="{json}" and no fill attribute. The JSON holds the stops and the matrix that maps the gradient's
    // unit square onto user space; the centre of the square is the centre of the gradient.
    public static partial class SvgParser
    {
        [Serializable] sealed class FigmaRgba { public float r, g, b, a = 1f; }
        [Serializable] sealed class FigmaStop { public FigmaRgba color; public float position; }
        [Serializable] sealed class FigmaMatrix { public float m00 = 1f, m01, m02, m10, m11 = 1f, m12; }

        [Serializable]
        sealed class FigmaGradient
        {
            public string type;
            public List<FigmaStop> stops;
            public FigmaMatrix transform;
            public float opacity = 1f;
        }

        /// <summary>The paint of a data-figma-gradient-fill / -stroke attribute, or null when absent or not angular / diamond.</summary>
        static VectraPaint FigmaGradientPaint(string json, Affine2 docToUser)
        {
            if (string.IsNullOrEmpty(json)) return null;
            FigmaGradient g;
            try { g = JsonUtility.FromJson<FigmaGradient>(json); }
            catch (ArgumentException) { return null; }
            if (g == null || g.stops == null || g.stops.Count == 0 || g.transform == null) return null;
            bool angular = g.type == "GRADIENT_ANGULAR", diamond = g.type == "GRADIENT_DIAMOND";
            if (!angular && !diamond) return null;

            var grad = new VectraGradient { interpolation = GradientInterpolation.Gamma };
            float last = 0f;
            foreach (var s in g.stops)
            {
                float pos = Mathf.Clamp01(Mathf.Max(last, s.position));
                last = pos;
                var c = s.color ?? new FigmaRgba();
                grad.stops.Add(new VectraGradientStop(pos, new Color(c.r, c.g, c.b, c.a)));
            }

            var m = g.transform;
            var unitToUser = new Affine2(m.m00, m.m10, m.m01, m.m11, m.m02, m.m12);
            if (math.abs(m.m00 * m.m11 - m.m01 * m.m10) < 1e-9f) return null;
            // Diamond: t reaches 1 where the diamond touches the middle of the box edges (0.5 from the centre).
            var centre = Affine2.Scale(new float2(diamond ? 2f : 1f)) * Affine2.Translate(new float2(-0.5f));
            return new VectraPaint
            {
                type = angular ? PaintType.AngularGradient : PaintType.DiamondGradient,
                gradient = grad,
                useCustomMapping = true,
                customMapping = centre * unitToUser.Inverse() * docToUser,
                opacity = Mathf.Clamp01(g.opacity)
            };
        }
    }
}
