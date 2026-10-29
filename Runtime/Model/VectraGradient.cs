using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace FirstSymbol.Vectra
{
    public enum GradientInterpolation
    {
        /// <summary>Interpolates the authored (sRGB) colours, like most design tools.</summary>
        Gamma,
        /// <summary>Interpolates in linear light (physically correct blending).</summary>
        Linear,
        /// <summary>Interpolates in OKLab (perceptually uniform, no muddy midpoints).</summary>
        OkLab,
        /// <summary>Hard colour steps without interpolation.</summary>
        Step
    }

    [Serializable]
    public struct VectraGradientStop
    {
        [Range(0f, 1f)] public float position;
        [ColorUsage(true, true)] public Color color;
        /// <summary>Position of the 50% blend between this stop and the next one, relative to their distance.</summary>
        [Range(0.01f, 0.99f)] public float midpoint;

        public VectraGradientStop(float position, Color color, float midpoint = 0.5f)
        {
            this.position = position;
            this.color = color;
            this.midpoint = midpoint;
        }
    }

    /// <summary>Gradient with an unlimited number of stops, HDR colours and selectable interpolation space.</summary>
    [Serializable]
    public class VectraGradient
    {
        public List<VectraGradientStop> stops = new List<VectraGradientStop>();
        public GradientInterpolation interpolation = GradientInterpolation.Gamma;

        public static VectraGradient Create(Color a, Color b) => new VectraGradient
        {
            stops = new List<VectraGradientStop> { new VectraGradientStop(0f, a), new VectraGradientStop(1f, b) }
        };

        public static VectraGradient Default() => Create(Color.white, Color.black);

        public static VectraGradient FromUnity(Gradient g)
        {
            var r = new VectraGradient { stops = new List<VectraGradientStop>() };
            if (g == null) return Default();
            var ck = g.colorKeys;
            foreach (var k in ck)
            {
                var c = k.color;
                c.a = g.Evaluate(k.time).a;
                r.stops.Add(new VectraGradientStop(k.time, c));
            }

            r.interpolation = g.mode == GradientMode.Fixed ? GradientInterpolation.Step : GradientInterpolation.Gamma;
            return r;
        }

        public VectraGradient Clone() => new VectraGradient
        {
            stops = new List<VectraGradientStop>(stops),
            interpolation = interpolation
        };

        /// <summary>Evaluates the gradient; the result is in the authored (gamma) colour space.</summary>
        public Color Evaluate(float t)
        {
            int n = stops?.Count ?? 0;
            if (n == 0) return Color.white;
            if (n == 1) return stops[0].color;

            // Stops are evaluated in sorted order without allocating: find the bracketing pair.
            int lo = -1, hi = -1;
            float loPos = float.MinValue, hiPos = float.MaxValue;
            int first = 0, last = 0;
            for (int i = 0; i < n; i++)
            {
                float p = stops[i].position;
                if (p < stops[first].position) first = i;
                if (p >= stops[last].position) last = i;
                if (p <= t && p >= loPos)
                {
                    loPos = p;
                    lo = i;
                }

                if (p > t && p < hiPos)
                {
                    hiPos = p;
                    hi = i;
                }
            }

            if (lo < 0) return stops[first].color;
            if (hi < 0) return stops[last].color;
            var a = stops[lo];
            var b = stops[hi];
            if (interpolation == GradientInterpolation.Step) return a.color;

            float f = (t - a.position) / math.max(b.position - a.position, 1e-6f);
            float m = math.clamp(a.midpoint <= 0f ? 0.5f : a.midpoint, 0.01f, 0.99f);
            f = f < m ? 0.5f * f / m : 0.5f + 0.5f * (f - m) / (1f - m);
            return Mix(a.color, b.color, f, interpolation);
        }

        public static Color Mix(Color a, Color b, float f, GradientInterpolation space)
        {
            switch (space)
            {
                case GradientInterpolation.Linear:
                    return Color.Lerp(a.linear, b.linear, f).gamma;
                case GradientInterpolation.OkLab:
                {
                    float3 la = ColorUtility2.LinearToOkLab(new float3(a.linear.r, a.linear.g, a.linear.b));
                    float3 lb = ColorUtility2.LinearToOkLab(new float3(b.linear.r, b.linear.g, b.linear.b));
                    float3 rgb = ColorUtility2.OkLabToLinear(math.lerp(la, lb, f));
                    return new Color(rgb.x, rgb.y, rgb.z, math.lerp(a.a, b.a, f)).gamma;
                }
                default:
                    return Color.Lerp(a, b, f);
            }
        }

        public int ComputeHash()
        {
            unchecked
            {
                int h = (int)interpolation * 7919;
                if (stops == null) return h;
                for (int i = 0; i < stops.Count; i++)
                {
                    var s = stops[i];
                    h = h * 31 + s.position.GetHashCode();
                    h = h * 31 + s.color.GetHashCode();
                    h = h * 31 + s.midpoint.GetHashCode();
                }

                return h;
            }
        }
    }

    /// <summary>Colour helpers shared by gradients and paints.</summary>
    public static class ColorUtility2
    {
        public static float3 LinearToOkLab(float3 c)
        {
            float l = 0.4122214708f * c.x + 0.5363325363f * c.y + 0.0514459929f * c.z;
            float m = 0.2119034982f * c.x + 0.6806995451f * c.y + 0.1073969566f * c.z;
            float s = 0.0883024619f * c.x + 0.2817188376f * c.y + 0.6299787005f * c.z;
            l = Cbrt(l);
            m = Cbrt(m);
            s = Cbrt(s);
            return new float3(
                0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        public static float3 OkLabToLinear(float3 c)
        {
            float l = c.x + 0.3963377774f * c.y + 0.2158037573f * c.z;
            float m = c.x - 0.1055613458f * c.y - 0.0638541728f * c.z;
            float s = c.x - 0.0894841775f * c.y - 1.2914855480f * c.z;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            return new float3(
                4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        static float Cbrt(float x) => math.sign(x) * math.pow(math.abs(x), 1f / 3f);

        /// <summary>Converts an authored colour to the project's working colour space.</summary>
        public static Color ToWorking(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        public static Color AdjustHsv(Color c, float hueShift, float saturation, float brightness, float contrast)
        {
            if (hueShift == 0f && saturation == 1f && brightness == 0f && contrast == 1f) return c;
            float a = c.a;
            float intensity = math.max(c.maxColorComponent, 1f);
            var ldr = intensity > 1f ? c / intensity : c;
            Color.RGBToHSV(ldr, out float h, out float s, out float v);
            h = (h + hueShift / 360f) % 1f;
            if (h < 0f) h += 1f;
            s = math.saturate(s * saturation);
            var r = Color.HSVToRGB(h, s, v) * intensity;
            r = new Color(
                (r.r - 0.5f) * contrast + 0.5f + brightness,
                (r.g - 0.5f) * contrast + 0.5f + brightness,
                (r.b - 0.5f) * contrast + 0.5f + brightness);
            r.r = math.max(r.r, 0f);
            r.g = math.max(r.g, 0f);
            r.b = math.max(r.b, 0f);
            r.a = a;
            return r;
        }
    }
}
