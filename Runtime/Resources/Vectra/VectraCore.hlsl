// Vectra — shared shader code: vertex layout decoding, signed distance functions, paints, layers and effects.
// The layout is mirrored by FirstSymbol.Vectra.Rendering.VectraEncoding (C#); keep both in sync.

#ifndef VECTRA_CORE_INCLUDED
#define VECTRA_CORE_INCLUDED

#include "VectraSdf.hlsl"

// Shape kinds (5 bits)
#define SHAPE_RECT 0
#define SHAPE_ELLIPSE 1
#define SHAPE_ARC 2
#define SHAPE_STAR 3
#define SHAPE_TRIANGLE 4
#define SHAPE_SEGMENT 5
#define SHAPE_CROSS 6
#define SHAPE_HEART 7
#define SHAPE_RHOMBUS 8
#define SHAPE_TRAPEZOID_SHAPE 9
#define SHAPE_PARALLELOGRAM 10
#define SHAPE_MOON 11
#define SHAPE_EGG 12
#define SHAPE_VESICA 13
#define SHAPE_SQUIRCLE 14
#define SHAPE_FLOWER 15
#define SHAPE_ARROW 16
#define SHAPE_CHEVRON 17
#define SHAPE_SDF_TEXTURE 28
#define SHAPE_PATH 29

// Layer kinds (4 bits)
#define LAYER_FILL 0
#define LAYER_STROKE 1
#define LAYER_SHADOW 2
#define LAYER_INNER_SHADOW 3
#define LAYER_BEVEL 4
#define LAYER_GRAIN 5

// Paint kinds (4 bits)
#define PAINT_SOLID 0
#define PAINT_LINEAR 1
#define PAINT_RADIAL 2
#define PAINT_ANGULAR 3
#define PAINT_DIAMOND 4
#define PAINT_FOCAL 5
#define PAINT_TEXTURE 6
#define PAINT_STRIPES 7
#define PAINT_CHECKER 8
#define PAINT_DOTS 9
#define PAINT_GRID 10
#define PAINT_NOISE 11
#define PAINT_FREEFORM 12
#define PAINT_CONTOUR 13

// Modifiers (3 bits)
#define MOD_NONE 0
#define MOD_CUT_X 1
#define MOD_CUT_Y 2
#define MOD_RADIAL 3
#define MOD_DISSOLVE 4
#define MOD_PIXELATE 5
#define MOD_DEFORM 6

sampler2D _MainTex;
// SRP Batcher shaders declare this inside their UnityPerMaterial buffer and define VECTRA_MATERIAL_DECLARED first.
#ifndef VECTRA_MATERIAL_DECLARED
float4 _MainTex_TexelSize;
#endif
sampler2D _VectraGradients;
float4 _VectraGradients_TexelSize;
sampler2D _VectraSdf;
float4 _VectraSdf_TexelSize;

struct VectraAttributes
{
    float4 vertex : POSITION;
    float4 color : COLOR;
    float4 uv0 : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float4 uv2 : TEXCOORD2;
    float4 uv3 : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VectraHeader
{
    uint shape;
    uint layer;
    uint paint;
    uint spread;
    uint modifier;
    bool modifierFlag;
    bool masked;
    uint style;
    bool additive;
};

VectraHeader DecodeHeader(float v)
{
    // Values are exact integers below 2^24; adding 0.5 would round to the wrong integer above 2^23.
    uint h = (uint)round(v);
    VectraHeader o;
    o.shape = h & 31u;
    o.layer = (h >> 5) & 15u;
    o.paint = (h >> 9) & 15u;
    o.spread = (h >> 13) & 3u;
    o.modifier = (h >> 15) & 7u;
    o.modifierFlag = ((h >> 18) & 1u) != 0u;
    o.masked = ((h >> 19) & 1u) != 0u;
    o.style = (h >> 20) & 7u;
    o.additive = ((h >> 23) & 1u) != 0u;
    return o;
}

// Two 12-bit unsigned values packed as hi * 4096 + lo, returned normalized to [0, 1].
float2 Unpack12(float v)
{
    float hi = floor(v * (1.0 / 4096.0));
    return float2(hi, v - hi * 4096.0) * (1.0 / 4095.0);
}

// Three 8-bit unsigned values packed as a * 65536 + b * 256 + c, returned normalized to [0, 1].
// Four 6-bit values packed as ((a * 64 + b) * 64 + c) * 64 + d, normalized to [0, 1].
float4 Unpack6(float v)
{
    float a = floor(v / 262144.0);
    float r = v - a * 262144.0;
    float b = floor(r / 4096.0);
    r -= b * 4096.0;
    float c = floor(r / 64.0);
    return float4(a, b, c, r - c * 64.0) * (1.0 / 63.0);
}

float3 Unpack8(float v)
{
    float a = floor(v * (1.0 / 65536.0));
    float r = v - a * 65536.0;
    float b = floor(r * (1.0 / 256.0));
    return float3(a, b, r - b * 256.0) * (1.0 / 255.0);
}

// Raw 12-bit codes of a packed pair (0..4095 each).
float2 Unpack12Raw(float v)
{
    float hi = floor(v * (1.0 / 4096.0));
    return float2(hi, v - hi * 4096.0);
}

// Unsigned logarithmic code: 0 = zero, 1..4095 = 2^[-12, 12].
float DecodeLog12(float code)
{
    return code < 0.5 ? 0.0 : exp2((code - 1.0) / 4094.0 * 24.0 - 12.0);
}

// Signed logarithmic code: bit 11 = sign, 0 = zero, 1..2047 = 2^[-10, 10].
float DecodeSignedLog12(float code)
{
    float neg = code >= 2048.0 ? 1.0 : 0.0;
    float m = code - neg * 2048.0;
    float v = m < 0.5 ? 0.0 : exp2((m - 1.0) / 2046.0 * 20.0 - 10.0);
    return neg > 0.5 ? -v : v;
}

// Distance from the baked SDF atlas. s1 = (texels per unit, texel offset x, texel offset y, distance range),
// s2.xy = first and last texel of the region, packed as 12-bit pairs. Texel coordinates survive atlas growth.
float SdTexture(float2 p, float4 s1, float2 s2)
{
    float2 t = p * s1.x + s1.yz;
    float2 tMin = Unpack12Raw(s2.x) + 0.5;
    float2 tMax = Unpack12Raw(s2.y) + 0.5;
    float2 tc = clamp(t, tMin, tMax);
    float d = tex2Dlod(_VectraSdf, float4(tc * _VectraSdf_TexelSize.xy, 0.0, 0.0)).r * s1.w;
    return d + length(t - tc) / max(s1.x, 1e-12);
}

// Single exit point: FXC reports the result of a long early-return chain as "potentially uninitialized".
float ShapeDistance(uint shape, uint style, float2 p, float4 s1, float4 s2)
{
    float d = 1e5;
    if (shape == SHAPE_RECT)
    {
        float m = min(s1.x, s1.y);
        float2 a = Unpack12(s1.z) * m;
        float2 b = Unpack12(s1.w) * m;
        d = SdRect(p, s1.xy, float4(a.x, a.y, b.x, b.y), style, s2.x);
    }
    else if (shape == SHAPE_ELLIPSE) d = SdEllipse(p, s1.xy);
    else if (shape == SHAPE_ARC) d = SdArc(p, s1.x, s1.y, s1.z, s1.w, style);
    else if (shape == SHAPE_STAR) d = SdStar(p, s1.x, s1.y, s1.z, s1.w, s2.x);
    else if (shape == SHAPE_TRIANGLE)
    {
        float2 hs = s1.xy;
        float2 a = (Unpack12(s1.z) * 2.0 - 1.0) * hs;
        float2 b = (Unpack12(s1.w) * 2.0 - 1.0) * hs;
        float2 c = (Unpack12(s2.x) * 2.0 - 1.0) * hs;
        d = SdTriangle(p, a, b, c) - s2.y;
    }
    else if (shape == SHAPE_SEGMENT) d = SdSegment(p, s1.xy, s1.zw, s2.x, style);
    else if (shape == SHAPE_CROSS) d = SdCross(p, s1.xy, s1.z, s1.w);
    else if (shape == SHAPE_HEART) d = SdHeart((p - float2(0.0, s1.y)) / s1.x) * s1.x;
    else if (shape == SHAPE_RHOMBUS) d = SdRhombus(p, s1.xy) - s1.z;
    else if (shape == SHAPE_TRAPEZOID_SHAPE) d = SdTrapezoid(p, s1.x, s1.y, s1.z) - s1.w;
    else if (shape == SHAPE_PARALLELOGRAM) d = SdParallelogram(p, s1.x, s1.y, s1.z) - s1.w;
    else if (shape == SHAPE_MOON) d = SdMoon(p, s1.z, s1.x, s1.y);
    else if (shape == SHAPE_EGG) d = SdEgg(p - float2(0.0, s1.z), s1.x, s1.y);
    else if (shape == SHAPE_VESICA) d = SdVesica(p, s1.x, s1.y);
    else if (shape == SHAPE_SQUIRCLE) d = SdSquircle(p, s1.xy, s1.z);
    else if (shape == SHAPE_FLOWER) d = SdFlower(p, s1.x, s1.y, s1.z, s1.w);
    else if (shape == SHAPE_ARROW) d = SdArrow(p, s1.x, s1.y, s1.z, s1.w);
    else if (shape == SHAPE_CHEVRON) d = SdChevron(p, s1.xy, s1.z);
    else if (shape == SHAPE_SDF_TEXTURE) d = SdTexture(p, s1, s2.xy);
    return d;
}

// Cuts applied to analytic distances (fill amount / progress).
float ApplyModifier(VectraHeader h, float d, float2 p, float s0)
{
    if (h.modifier == MOD_CUT_X)
        return max(d, h.modifierFlag ? (s0 - p.x) : (p.x - s0));
    if (h.modifier == MOD_CUT_Y)
        return max(d, h.modifierFlag ? (s0 - p.y) : (p.y - s0));
    if (h.modifier == MOD_RADIAL)
    {
        float2 r = Unpack12(s0);
        float start = r.x * VECTRA_TAU;
        float sweep = r.y * VECTRA_TAU;
        if (sweep >= VECTRA_TAU - 1e-3) return d;
        if (sweep <= 1e-4) return 1e5;
        float ha = sweep * 0.5;
        // aperture centred at start + ha, measured counter-clockwise from +x; flag = clockwise
        float centre = h.modifierFlag ? (start - ha) : (start + ha);
        float2 q = Rotate(p, centre - VECTRA_PI * 0.5);
        q.x = abs(q.x);
        return max(d, SdWedge(q, ha));
    }
    return d;
}

// ---------------------------------------------------------------------------------------------------------------
// Paints
// ---------------------------------------------------------------------------------------------------------------

float SpreadT(float t, uint spread)
{
    if (spread == 1u) return frac(t);
    if (spread == 2u) return 1.0 - abs(VMod(t, 2.0) - 1.0);
    return saturate(t);
}

float4 SampleGradient(float t, float v)
{
    float w = _VectraGradients_TexelSize.z;
    float u = (t * (w - 1.0) + 0.5) * _VectraGradients_TexelSize.x;
    return tex2Dlod(_VectraGradients, float4(u, v, 0.0, 0.0));
}

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float Fbm(float2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * ValueNoise(p);
        p = p * 2.03 + 17.1;
        a *= 0.5;
    }
    return v / 0.9375;
}

// g = paint coordinates, v = gradient row (or block start), w = paint parameter.
float4 EvaluatePaint(VectraHeader h, float2 g, float2 dgdx, float2 dgdy, float v, float w, float4 vertexColor)
{
    uint kind = h.paint;
    if (kind == PAINT_SOLID) return float4(vertexColor.rgb * w, vertexColor.a);
    if (kind == PAINT_TEXTURE)
    {
        float2 uv = h.spread == 1u ? frac(g) : g;
        return tex2Dgrad(_MainTex, uv, dgdx, dgdy) * vertexColor;
    }

    float t = 0.0;
    float2 aa = max(float2(length(float2(dgdx.x, dgdy.x)), length(float2(dgdx.y, dgdy.y))), 1e-6);
    if (kind == PAINT_LINEAR) t = g.x;
    else if (kind == PAINT_RADIAL) t = length(g);
    else if (kind == PAINT_ANGULAR) t = frac(atan2(g.y, g.x) / VECTRA_TAU + 1.0);
    else if (kind == PAINT_DIAMOND) t = abs(g.x) + abs(g.y);
    else if (kind == PAINT_FOCAL)
    {
        float2 f = Unpack12(w) * 2.0 - 1.0;
        float2 dir = g - f;
        float a = dot(dir, dir);
        if (a < 1e-12) t = 0.0;
        else
        {
            float b = 2.0 * dot(f, dir);
            float c = dot(f, f) - 1.0;
            float disc = max(b * b - 4.0 * a * c, 0.0);
            float s = (-b + sqrt(disc)) / (2.0 * a);
            t = s > 1e-6 ? 1.0 / s : 1.0;
        }
    }
    else if (kind == PAINT_STRIPES)
    {
        float f = frac(g.x - w * 0.5 + 0.5) - 0.5;
        t = 1.0 - saturate(0.5 + (w * 0.5 - abs(f)) / aa.x);
    }
    else if (kind == PAINT_CHECKER)
    {
        float2 q = frac(g * 0.5) * 2.0 - 1.0;
        float2 s = saturate(0.5 + (abs(q) - 0.5) / aa);
        t = s.x * (1.0 - s.y) + s.y * (1.0 - s.x);
    }
    else if (kind == PAINT_DOTS)
    {
        float2 cell = frac(g) - 0.5;
        t = 1.0 - saturate(0.5 + (w * 0.5 - length(cell)) / max(aa.x, aa.y));
    }
    else if (kind == PAINT_GRID)
    {
        float2 f = abs(frac(g + 0.5) - 0.5);
        float2 l = saturate(0.5 + (w * 0.5 - f) / aa);
        t = 1.0 - max(l.x, l.y);
    }
    else if (kind == PAINT_NOISE) t = Fbm(g);
    else if (kind == PAINT_FREEFORM)
    {
        float2 q = saturate(g);
        float wTex = _VectraGradients_TexelSize.z;
        float u = (q.x * (wTex - 1.0) + 0.5) * _VectraGradients_TexelSize.x;
        return tex2Dlod(_VectraGradients, float4(u, v + q.y * w, 0.0, 0.0)) * vertexColor;
    }

    if (kind <= PAINT_FOCAL) t = SpreadT(t, h.spread);
    return SampleGradient(t, v) * vertexColor;
}

// ---------------------------------------------------------------------------------------------------------------
// Fragment core
// ---------------------------------------------------------------------------------------------------------------

struct VectraFragmentInput
{
    float2 p;      // shape space position
    float2 g;      // paint coordinates
    float4 hdr;    // header, s0, paint v, paint param
    float4 s1;
    float4 s2;
    float4 color;
};

// Signed distance (outside positive, in pixels) to one side of a path trapezoid. The side is the part of the line
// dot(n, p) + c = 0 between yb and yt; beyond its ends the distance grows towards the end points, which keeps the
// horizontal anti-aliasing fringe of shallow edges from leaking past the segment.
float SideDistancePx(float2 p, float angle, float c, float yb, float yt, float2 dpdx, float2 dpdy)
{
    float2 n;
    sincos(angle, n.y, n.x);
    float d = dot(n, p) + c;
    float2 t = float2(-n.y, n.x);
    float2 p0 = -c * n;
    float s = dot(p - p0, t);
    if (abs(t.y) > 1e-6)
    {
        float sb = (yb - p0.y) / t.y;
        float st = (yt - p0.y) / t.y;
        s = clamp(s, min(sb, st), max(sb, st));
    }
    float2 q = p0 + t * s;
    float2 v = p - q;
    float dist = length(v) * (d < 0.0 ? -1.0 : 1.0);
    // Pixel scale of the distance along the side normal.
    float w = max(length(float2(dot(n, dpdx), dot(n, dpdy))), 1e-6);
    return clamp(dist / w, -64.0, 64.0);
}

// Coverage of a path trapezoid with analytic anti-aliasing of its boundary sides.
// s1 = (left normal angle, left offset, right normal angle, right offset), s2.xy = (top y, bottom y),
// style bit 0 / bit 1 = top / bottom side is a boundary. Internal sides carry an offset of VectraEncoding.Inside.
float TrapezoidCoverage(float2 p, float4 s1, float2 s2, uint style, float2 dpdx, float2 dpdy, float px)
{
    float c = 1.0;
    if (s1.y > -1e5) c *= Coverage(SideDistancePx(p, s1.x, s1.y, s2.y, s2.x, dpdx, dpdy) * px, 0.0, px);
    if (s1.w > -1e5) c *= Coverage(SideDistancePx(p, s1.z, s1.w, s2.y, s2.x, dpdx, dpdy) * px, 0.0, px);
    float wy = max(length(float2(dpdx.y, dpdy.y)), 1e-6);
    if ((style & 1u) != 0u) c *= Coverage(clamp((p.y - s2.x) / wy, -64.0, 64.0) * px, 0.0, px);
    if ((style & 2u) != 0u) c *= Coverage(clamp((s2.y - p.y) / wy, -64.0, 64.0) * px, 0.0, px);
    return c;
}

float4 VectraShade(VectraFragmentInput i)
{
    VectraHeader h = DecodeHeader(i.hdr.x);
    float2 p = i.p;
    float2 dpdx = ddx(p), dpdy = ddy(p);
    float2 dgdx = ddx(i.g), dgdy = ddy(i.g);
    float px = max(sqrt(abs(dpdx.x * dpdy.y - dpdx.y * dpdy.x)), 1e-7);

    float alpha;
    float4 color;
    if (h.shape == SHAPE_PATH)
    {
        alpha = TrapezoidCoverage(p, i.s1, i.s2.xy, h.style, dpdx, dpdy, px);
        if (h.masked)
        {
            float2 mt = p * i.hdr.y + i.s2.zw;
            alpha *= saturate(tex2Dlod(_VectraSdf, float4(mt * _VectraSdf_TexelSize.xy, 0.0, 0.0)).r);
        }
        color = EvaluatePaint(h, i.g, dgdx, dgdy, i.hdr.z, i.hdr.w, i.color);
        if (h.modifier == MOD_DISSOLVE)
        {
            float3 dv = Unpack8(i.hdr.y);
            float n = ValueNoise(p / max(exp2(dv.z * 16.0 - 4.0), 1e-4));
            alpha *= smoothstep(dv.x - dv.y * 0.5, dv.x + dv.y * 0.5 + 1e-4, n);
        }
    }
    else
    {
        if (h.modifier == MOD_PIXELATE)
        {
            // hdr.y = cell size in shape units: sample the outline at cell centres, without anti-aliasing.
            float cell = max(i.hdr.y, 1e-4);
            p = (floor(p / cell) + 0.5) * cell;
            px = min(px, cell * 1e-3);
        }
        else if (h.modifier == MOD_DEFORM)
        {
            // hdr.y = (log amplitude, log wavelength, drift, phase); waves travel with time, no rebuild needed.
            float4 q = Unpack6(i.hdr.y);
            float amp = exp2(q.x * 14.0 - 4.0);
            float k = VECTRA_TAU / exp2(q.y * 14.0 - 2.0);
            float ph = q.w * VECTRA_TAU + (q.z * 8.0 - 4.0) * _Time.y * VECTRA_TAU;
            p += amp * float2(sin(p.y * k + ph), cos(p.x * k + ph * 1.3));
        }

        float d = ShapeDistance(h.shape, h.style, p, i.s1, i.s2);
        d = ApplyModifier(h, d, p, i.hdr.y);
        float2 lp = i.s2.zw;
        color = EvaluatePaint(h, i.g, dgdx, dgdy, i.hdr.z, i.hdr.w, i.color);
        // Contour gradients: position along the gradient = depth below the outline / span (paint param).
        if (h.paint == PAINT_CONTOUR)
            color = SampleGradient(SpreadT(-d / max(i.hdr.w, 1e-5), h.spread), i.hdr.z) * i.color;

        if (h.layer == LAYER_FILL)
        {
            alpha = Coverage(d, lp.x, px);
        }
        else if (h.layer == LAYER_STROKE)
        {
            alpha = Coverage(abs(d - lp.y) - lp.x, 0.0, px);
        }
        else if (h.layer == LAYER_SHADOW)
        {
            // lp = (sigma, spread); spread bit 0 = glow falloff (smoother, wider knee). Shadows are solid-painted,
            // so the spread bits are free for this flag.
            float dd = d - lp.y;
            bool glow = (h.spread & 1u) != 0u;
            alpha = glow ? saturate(1.0 - dd / max(lp.x * 2.0, px)) : Coverage(dd, lp.x, px);
            if (glow) alpha = alpha * alpha * (3.0 - 2.0 * alpha);
        }
        else if (h.layer == LAYER_INNER_SHADOW)
        {
            // lp.x = packed signed-log offset (x, y), lp.y = packed log (sigma, choke)
            float2 oc = Unpack12Raw(lp.x);
            float2 off = float2(DecodeSignedLog12(oc.x), DecodeSignedLog12(oc.y));
            float2 scc = Unpack12Raw(lp.y);
            float2 sc = float2(DecodeLog12(scc.x), DecodeLog12(scc.y));
            float inside = Coverage(d, 0.0, px);
            float ds = ShapeDistance(h.shape, h.style, p - off, i.s1, i.s2);
            ds = ApplyModifier(h, ds, p - off, i.hdr.y);
            alpha = inside * (1.0 - Coverage(ds + sc.y, sc.x, px));
        }
        else if (h.layer == LAYER_BEVEL)
        {
            // lp.x = packed (log size, light angle), lp.y = packed (intensity, softness)
            float2 a = Unpack12Raw(lp.x);
            float2 b = Unpack12(lp.y);
            float size = max(DecodeLog12(a.x), px);
            float angle = a.y / 4095.0 * VECTRA_TAU;
            float e = max(px * 0.5, size * 0.02);
            float gx = ShapeDistance(h.shape, h.style, p + float2(e, 0.0), i.s1, i.s2) - d;
            float gy = ShapeDistance(h.shape, h.style, p + float2(0.0, e), i.s1, i.s2) - d;
            float2 grad = normalize(float2(gx, gy) + 1e-9);
            float band = saturate(1.0 + d / size);
            band = pow(band, lerp(1.0, 3.0, b.y));
            float2 light = float2(cos(angle), sin(angle));
            float lit = dot(-grad, light) * band;
            float inside = Coverage(d, 0.0, px);
            alpha = inside * saturate(abs(lit)) * b.x;
            color = lit >= 0.0 ? float4(color.rgb, color.a) : float4(0.0, 0.0, 0.0, color.a);
        }
        else // LAYER_GRAIN: lp = (amount, grain size in pixels)
        {
            float inside = Coverage(d, 0.0, px);
            if (lp.y < 0.0)
            {
                // Monotone speckles in the paint colour, cells in local units: lp.y = -(16 * size + density)
                float v = -lp.y;
                float s16 = floor(v);
                float2 mcell = floor(p / max(s16 * 0.0625, 1e-3));
                alpha = inside * step(Hash21(mcell), v - s16);
            }
            else
            {
                float2 cell = floor(p / (px * max(lp.y, 1.0)));
                float n = Hash21(cell) * 2.0 - 1.0;
                alpha = inside * abs(n) * lp.x;
                color = n >= 0.0 ? float4(1.0, 1.0, 1.0, color.a) : float4(0.0, 0.0, 0.0, color.a);
            }
        }

        if (h.modifier == MOD_DISSOLVE)
        {
            float3 dv = Unpack8(i.hdr.y);
            float n = ValueNoise(p / max(exp2(dv.z * 16.0 - 4.0), 1e-4));
            alpha *= smoothstep(dv.x - dv.y * 0.5, dv.x + dv.y * 0.5 + 1e-4, n);
        }
    }

    // Gradients are dithered by +-0.5 of an 8-bit step to hide banding.
    if ((h.paint >= PAINT_LINEAR && h.paint <= PAINT_FOCAL) || h.paint >= PAINT_FREEFORM)
        color.rgb += (Hash21(floor(i.p / px)) - 0.5) * (1.0 / 255.0);

    color.a *= alpha;
    color.rgb *= color.a;
    if (h.additive) color.a = 0.0;
    return color;
}

#endif
