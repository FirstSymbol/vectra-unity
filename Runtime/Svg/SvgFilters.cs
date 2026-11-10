using System.Collections.Generic;
using UnityEngine;

namespace FirstSymbol.Vectra.Svg
{
    // Recogniser for the filter chains Figma writes (drop shadow, inner shadow, layer blur, noise). It is not a filter graph:
    // it reads the primitives in order and turns each recognised group into one native effect. Whatever it cannot place is
    // reported and left out, so the element itself is always drawn.
    public static partial class SvgParser
    {
        /// <summary>
        /// Figma draws a blur with a Gaussian of 5/6 of the standard deviation its SVG export writes (radius / 2): fitting its own PNG renders of
        /// shadows, inner shadows and layer blurs against exact Gaussians of the exported value gives the least error at 0.83 every time
        /// (mean error 0.06-0.13 levels against 0.43-0.76 at 1.0). Chrome, which follows the SVG, blurs 20% wider than Figma does.
        /// </summary>
        const float FigmaBlurScale = 5f / 6f;

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

        /// <summary>
        /// With <paramref name="steps"/> the shadows and the blur of the filter are not turned into native effects: they are listed as
        /// steps for the layer compositor, which computes them from the picture of the whole subtree (everything else still goes to
        /// <paramref name="effects"/>).
        /// </summary>
        static void AddRichFilter(SvgNode f, float scale, List<VectraEffect> effects, List<SvgFilterStep> steps, Context ctx)
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
                        float s = (sd.Count > 0 ? sd[0] : 0f) * FigmaBlurScale;
                        if (sh.active) sh.blur = s;
                        else if (s > 0f)
                        {
                            if (steps != null) steps.Add(new SvgFilterStep { kind = SvgFilterKind.Blur, sigma = s * scale });
                            else effects.Add(new LayerBlurEffect { blur = s * 2f * scale });
                        }

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
                        if (!sh.active)
                        {
                            // The shape itself (in="SourceGraphic") or the empty background.
                            if (steps != null && inAttr == "SourceGraphic") steps.Add(new SvgFilterStep { kind = SvgFilterKind.Source });
                            break;
                        }

                        string mode = p.Attr("mode", "normal");
                        if (steps != null)
                        {
                            // Figma writes in2="BackgroundImageFix" (an empty flood), which in strict SVG makes any mode a no-op; Figma itself
                            // blends the shadow with what is below the layer, so the mode goes to the compositor as it is.
                            steps.Add(new SvgFilterStep
                            {
                                kind = sh.inner ? SvgFilterKind.InnerShadow : SvgFilterKind.DropShadow,
                                color = sh.color, dx = sh.dx * scale, dy = sh.dy * scale, sigma = sh.blur * scale,
                                spread = sh.spread * scale, choke = sh.choke * scale, blend = SvgLayers.ParseBlend(mode)
                            });
                            sh = new FigmaShadow();
                            break;
                        }

                        // Vectra layers blend normally or additively; screen is the nearest of the CSS modes to additive.
                        var blend = LayerBlend.Normal;
                        if (mode == "screen" || mode == "plus-lighter") blend = LayerBlend.Additive;
                        else if (mode != "normal")
                            ctx.Warn("SVG: the shadow blend mode '" + mode + "' is drawn as normal.");
                        if (sh.inner)
                        {
                            var inner = new InnerShadowEffect
                            {
                                color = sh.color, offset = new Vector2(sh.dx, sh.dy) * scale, blur = sh.blur * 2f * scale,
                                choke = sh.choke * scale,
                                blend = blend
                            };
                            effects.Add(inner);
                            if (mode == "screen") ctx.screenBlended.Add(inner);
                        }
                        else
                            effects.Add(new DropShadowEffect
                            {
                                color = sh.color, offset = new Vector2(sh.dx, sh.dy) * scale, blur = sh.blur * 2f * scale,
                                spread = sh.spread * scale,
                                blend = blend
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
            if (steps != null) EnsureSource(steps);
        }

        /// <summary>A chain without the SourceGraphic blend still draws the shape: it goes before the first inner shadow or blur, else last.</summary>
        static void EnsureSource(List<SvgFilterStep> steps)
        {
            if (steps.Count == 0) return;
            int at = steps.Count;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].kind == SvgFilterKind.Source) return;
                if (steps[i].kind != SvgFilterKind.DropShadow && at == steps.Count) at = i;
            }

            steps.Insert(at, new SvgFilterStep { kind = SvgFilterKind.Source });
        }

        // What native, resolution-free effects can draw for a single shape. Everything else needs the layer compositor.
        static bool CanDrawNatively(List<SvgFilterStep> steps, SvgElementData e)
        {
            if (e.hasFill && e.hasStroke) return false; // the shadow of fill and stroke together is the union, not the fill's
            foreach (var s in steps)
            {
                switch (s.kind)
                {
                    case SvgFilterKind.DropShadow:
                        if (s.blend != SvgBlendMode.Normal) return false;
                        break;
                    case SvgFilterKind.InnerShadow:
                        // Screen over a solid fill is not identical to the additive native effect (the native blur is an approximation near corners).
                        if (s.blend != SvgBlendMode.Normal) return false;
                        break;
                }
            }

            return true;
        }

        static void AddNativeEffects(List<SvgFilterStep> steps, SvgElementData e)
        {
            foreach (var s in steps)
            {
                switch (s.kind)
                {
                    case SvgFilterKind.DropShadow:
                        e.effects.Add(new DropShadowEffect
                        {
                            color = s.color, offset = new Vector2(s.dx, s.dy), blur = s.sigma * 2f, spread = s.spread
                        });
                        break;
                    case SvgFilterKind.InnerShadow:
                        e.effects.Add(new InnerShadowEffect { color = s.color, offset = new Vector2(s.dx, s.dy), blur = s.sigma * 2f, choke = s.choke });
                        break;
                    case SvgFilterKind.Blur:
                        e.effects.Add(new LayerBlurEffect { blur = s.sigma * 2f });
                        break;
                }
            }
        }

        /// <summary>The steps drawn as native effects of every shape of a subtree, the way a filter was handled before layers (best effort).</summary>
        internal static void StepsToEffects(List<SvgFilterStep> steps, List<VectraEffect> effects)
        {
            var probe = new SvgElementData();
            AddNativeEffects(steps.FindAll(s => s.kind != SvgFilterKind.InnerShadow || s.blend == SvgBlendMode.Normal), probe);
            effects.AddRange(probe.effects);
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
