using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    // Recogniser for the filter chains Figma writes (drop shadow, inner shadow, layer blur, noise). It is not a filter graph:
    // it reads the primitives in order and turns each recognised group into one native effect. Whatever it cannot place is
    // reported and left out, so the element itself is always drawn.
    public static partial class SvgParser
    {
        sealed class FigmaShadow
        {
            public bool active, inner, haveColor;
            public float dx, dy, blur, spread, choke;
            public Color color = new Color(0f, 0f, 0f, 0.25f);
        }

        sealed class FigmaNoise
        {
            public bool active, haveColor;
            public float frequency = 0.5f, density = 0.25f;
            public Color color = new Color(0f, 0f, 0f, 0.25f);
        }

        /// <summary>True when the filter uses primitives that only the Figma recogniser reads.</summary>
        static bool IsRichFilter(SvgNode f)
        {
            foreach (var p in f.children)
                switch (p.name)
                {
                    case "feBlend": case "feComposite": case "feMorphology": case "feTurbulence": case "feComponentTransfer":
                        return true;
                    case "feColorMatrix":
                        if (p.Attr("type", "matrix") != "saturate" && p.Attr("type", "matrix") != "hueRotate") return true;
                        break;
                }

            return false;
        }

        static void AddRichFilter(SvgNode f, float scale, List<VectraEffect> effects, Context ctx)
        {
            var sh = new FigmaShadow();
            var noise = new FigmaNoise();
            foreach (var p in f.children)
            {
                if (p.style == null) ComputeStyles(p, f.style, ctx);
                string inAttr = p.Attr("in");
                switch (p.name)
                {
                    case "feFlood":
                    {
                        var c = ColorOf(Style(p, "flood-color", p.Attr("flood-color") ?? "black"), p);
                        c.a *= Number(Style(p, "flood-opacity", p.Attr("flood-opacity")), 1f);
                        if (noise.active && p.Attr("result") != "BackgroundImageFix")
                        {
                            noise.color = c;
                            noise.haveColor = true;
                        }

                        break;
                    }
                    case "feColorMatrix":
                    {
                        string type = p.Attr("type", "matrix");
                        var v = Numbers(p.Attr("values") ?? "");
                        if (type == "saturate" && v.Count > 0) effects.Add(new ColorAdjustEffect { saturation = v[0] });
                        else if (type == "hueRotate" && v.Count > 0) effects.Add(new ColorAdjustEffect { hueShift = v[0] });
                        else if (type == "luminanceToAlpha") { }
                        else if (type == "matrix" && v.Count >= 20)
                        {
                            if (inAttr == "SourceAlpha")
                            {
                                sh = new FigmaShadow { active = true };
                            }
                            else if (sh.active)
                            {
                                sh.color = new Color(v[4], v[9], v[14], v[18]);
                                sh.haveColor = true;
                            }
                            else ctx.Warn("SVG: a feColorMatrix that is not part of a shadow is not supported; it is ignored.");
                        }
                        else ctx.Warn("SVG: feColorMatrix type '" + type + "' is not supported; it is ignored.");

                        break;
                    }
                    case "feMorphology":
                    {
                        if (inAttr != "SourceAlpha" && !sh.active)
                        {
                            ctx.Warn("SVG: feMorphology outside a shadow is not supported; it is ignored.");
                            break;
                        }

                        sh.active = true;
                        var r = Numbers(p.Attr("radius") ?? "0");
                        float radius = r.Count > 0 ? Mathf.Max(0f, r[0]) : 0f;
                        if (p.Attr("operator") == "erode") sh.choke = radius;
                        else sh.spread = radius;
                        break;
                    }
                    case "feOffset":
                        sh.dx = Number(p.Attr("dx"), 0f);
                        sh.dy = Number(p.Attr("dy"), 0f);
                        break;
                    case "feGaussianBlur":
                    {
                        var sd = Numbers(p.Attr("stdDeviation") ?? "0");
                        float s = sd.Count > 0 ? sd[0] : 0f;
                        if (sh.active) sh.blur = s;
                        else if (s > 0f) effects.Add(new LayerBlurEffect { blur = s * 2f * scale });
                        break;
                    }
                    case "feComposite":
                    {
                        string op = p.Attr("operator", "over");
                        if (op == "arithmetic" && sh.active && Number(p.Attr("k2"), 0f) < 0f && Number(p.Attr("k3"), 0f) > 0f)
                            sh.inner = true;
                        else if (op == "in" && noise.active) { }
                        else if (op == "in" && inAttr != null && inAttr.StartsWith("color")) { }
                        else ctx.Warn("SVG: feComposite operator '" + op + "' is not supported; it is ignored.");
                        break;
                    }
                    case "feBlend":
                    {
                        if (!sh.active) break; // the shape itself (in="SourceGraphic") or the empty background
                        string mode = p.Attr("mode", "normal");
                        if (mode != "normal")
                            ctx.Warn("SVG: the shadow blend mode '" + mode + "' is drawn as normal.");
                        if (sh.inner)
                            effects.Add(new InnerShadowEffect
                            {
                                color = sh.color, offset = new Vector2(sh.dx, sh.dy) * scale, blur = sh.blur * 2f * scale,
                                choke = sh.choke * scale
                            });
                        else
                            effects.Add(new DropShadowEffect
                            {
                                color = sh.color, offset = new Vector2(sh.dx, sh.dy) * scale, blur = sh.blur * 2f * scale,
                                spread = sh.spread * scale
                            });
                        sh = new FigmaShadow();
                        break;
                    }
                    case "feTurbulence":
                        FlushNoise(noise, scale, effects);
                        noise = new FigmaNoise { active = true };
                        var bf = Numbers(p.Attr("baseFrequency") ?? "0.5");
                        if (bf.Count > 0 && bf[0] > 0f) noise.frequency = bf[0];
                        break;
                    case "feComponentTransfer":
                    {
                        foreach (var fn in p.children)
                        {
                            if (fn.name != "feFuncA") continue;
                            var t = Numbers(fn.Attr("tableValues") ?? "");
                            if (t.Count == 0) continue;
                            noise.density = NoiseCoverage(t);
                        }

                        break;
                    }
                    case "feMerge":
                        FlushNoise(noise, scale, effects);
                        noise = new FigmaNoise();
                        break;
                }
            }

            FlushNoise(noise, scale, effects);
            if (sh.active) ctx.Warn("SVG: a shadow in a filter has no closing feBlend and is ignored.");
        }

        // Share of the area where a discrete alpha table lets the noise through. fractalNoise luminance is roughly normal around
        // 0.5; sigma 0.1 reproduces the coverage measured in Figma's own render of the 100-entry table it writes.
        static float NoiseCoverage(List<float> table)
        {
            const float sigma = 0.1f;
            float total = 0f;
            for (int i = 0; i < table.Count; i++)
                if (table[i] >= 0.5f)
                    total += NormalCdf(((i + 1f) / table.Count - 0.5f) / sigma) - NormalCdf(((float)i / table.Count - 0.5f) / sigma);
            return total;
        }

        static float NormalCdf(float z)
        {
            // Abramowitz-Stegun 7.1.26
            float x = Mathf.Abs(z) / 1.41421356f, t = 1f / (1f + 0.3275911f * x);
            float erf = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
            return 0.5f * (1f + (z >= 0f ? erf : -erf));
        }

        static void FlushNoise(FigmaNoise n, float scale, List<VectraEffect> effects)
        {
            if (!n.active || !n.haveColor) return;
            effects.Add(new GrainEffect
            {
                monotone = true,
                color = n.color,
                density = Mathf.Clamp01(n.density),
                size = 0.5f / n.frequency
            });
        }
    }
}
