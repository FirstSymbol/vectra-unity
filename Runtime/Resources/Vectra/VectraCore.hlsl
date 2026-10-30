// Vectra — shared shader code: vertex layout decoding, signed distance functions, paints, layers and effects.
// The layout is mirrored by FirstSymbol.Vectra.Rendering.VectraEncoding (C#); keep both in sync.

#ifndef VECTRA_CORE_INCLUDED
#define VECTRA_CORE_INCLUDED

#define VECTRA_PI 3.14159265359
#define VECTRA_TAU 6.28318530718

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

// Modifiers (3 bits)
#define MOD_NONE 0
#define MOD_CUT_X 1
#define MOD_CUT_Y 2
#define MOD_RADIAL 3
#define MOD_DISSOLVE 4
#define MOD_PIXELATE 5

sampler2D _MainTex;
float4 _MainTex_TexelSize;
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

float VMod(float x, float y) { return x - y * floor(x / y); }
float Dot2(float2 v) { return dot(v, v); }
float NDot(float2 a, float2 b) { return a.x * b.x - a.y * b.y; }

float2 Rotate(float2 p, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float2(c * p.x + s * p.y, -s * p.x + c * p.y);
}

// Approximation of erf with |error| < 2e-4.
float Erf(float x)
{
    // Clamp first: distances of "never anti-aliased" sides are huge and would overflow to inf/NaN.
    x = clamp(x, -6.0, 6.0);
    float x2 = x * x;
    float a = 0.147;
    float t = x2 * (1.2732395 + a * x2) / (1.0 + a * x2);
    return sign(x) * sqrt(saturate(1.0 - exp(-t)));
}

// Coverage of a point at signed distance d (negative inside) for a blur sigma, both in local units.
// px is the size of one screen pixel in local units; edges always get at least ~1px of anti-aliasing.
float Coverage(float d, float sigma, float px)
{
    float s = max(sigma, px * 0.42);
    float x = clamp(d / max(s * 1.41421356, 1e-12), -6.0, 6.0);
    return saturate(0.5 - 0.5 * Erf(x));
}

// ---------------------------------------------------------------------------------------------------------------
// Signed distance functions (negative inside). Most are adapted from Inigo Quilez's 2D distance functions (MIT).
// ---------------------------------------------------------------------------------------------------------------

float SdBox(float2 p, float2 b)
{
    float2 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
}

// r = (top-right, bottom-right, top-left, bottom-left)
float SdRect(float2 p, float2 b, float4 r, uint style, float smoothing)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    float rr = (p.y > 0.0) ? r.x : r.y;
    float2 ap = abs(p);
    if (style == 1u)
    {
        float ch = (ap.x + ap.y - (b.x + b.y - rr)) * 0.70710678;
        return max(SdBox(p, b), ch);
    }
    if (style == 2u)
        return max(SdBox(p, b), rr - length(ap - b));
    if (style == 3u)
        return max(SdBox(p, b), -SdBox(ap - b, float2(rr, rr)));
    float2 q = ap - b + rr;
    if (style == 4u && rr > 1e-5)
    {
        float n = lerp(2.0, 6.0, saturate(smoothing));
        float2 m = max(q, 0.0) / rr;
        float l = pow(pow(m.x, n) + pow(m.y, n), 1.0 / n) * rr;
        return min(max(q.x, q.y), 0.0) + l - rr;
    }
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - rr;
}

float SdEllipse(float2 p, float2 ab)
{
    if (abs(ab.x - ab.y) <= 1e-4 * max(ab.x, ab.y)) return length(p) - ab.x;
    p = abs(p);
    if (p.x > p.y) { p = p.yx; ab = ab.yx; }
    float l = ab.y * ab.y - ab.x * ab.x;
    float m = ab.x * p.x / l;
    float m2 = m * m;
    float n = ab.y * p.y / l;
    float n2 = n * n;
    float c = (m2 + n2 - 1.0) / 3.0;
    float c3 = c * c * c;
    float q = c3 + m2 * n2 * 2.0;
    float d = c3 + m2 * n2;
    float g = m + m * n2;
    float co;
    if (d < 0.0)
    {
        float h = acos(clamp(q / c3, -1.0, 1.0)) / 3.0;
        float s = cos(h);
        float t = sin(h) * sqrt(3.0);
        float rx = sqrt(max(-c * (s + t + 2.0) + m2, 0.0));
        float ry = sqrt(max(-c * (s - t + 2.0) + m2, 0.0));
        co = (ry + sign(l) * rx + abs(g) / max(rx * ry, 1e-12) - m) / 2.0;
    }
    else
    {
        float h = 2.0 * m * n * sqrt(d);
        float s = sign(q + h) * pow(abs(q + h), 1.0 / 3.0);
        float u = sign(q - h) * pow(abs(q - h), 1.0 / 3.0);
        float rx = -s - u - c * 4.0 + 2.0 * m2;
        float ry = (s - u) * sqrt(3.0);
        float rm = sqrt(rx * rx + ry * ry);
        co = (ry / sqrt(max(rm - rx, 1e-12)) + 2.0 * g / max(rm, 1e-12) - m) / 2.0;
    }
    co = saturate(co);
    float2 r = ab * float2(co, sqrt(1.0 - co * co));
    return length(r - p) * sign(p.y - r.y);
}

// Signed distance to a wedge of half-aperture ha centred on +y (p.x already folded to >= 0).
float SdWedge(float2 p, float ha)
{
    float2 sc = float2(sin(ha), cos(ha));
    float2 n = float2(sc.y, -sc.x);
    float dRay = dot(p, sc) > 0.0 ? abs(dot(p, n)) : length(p);
    float a = atan2(p.x, p.y);
    return a < ha ? -dRay : dRay;
}

// Ring / arc / pie. ra = centre radius, rb = half thickness, ha = half aperture, rot = rotation of the aperture
// centre measured from +y towards -x (counter-clockwise). style: 0 = butt caps, 1 = round caps.
float SdArc(float2 p, float ra, float rb, float ha, float rot, uint style)
{
    p = Rotate(p, rot);
    float inner = ra - rb;
    float ring = inner <= 1e-5 ? length(p) - (ra + rb) : abs(length(p) - ra) - rb;
    if (ha >= VECTRA_PI - 1e-4) return ring;
    p.x = abs(p.x);
    if (style == 1u)
    {
        float2 sc = float2(sin(ha), cos(ha));
        return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
    }
    return max(ring, SdWedge(p, ha));
}

// Star / regular polygon. k = inner radius ratio (cos(pi/n) gives a regular polygon).
float SdStar(float2 p, float R, float n, float k, float rot, float rounding)
{
    p = Rotate(p, rot);
    float an = VECTRA_PI / max(n, 2.0);
    float a = atan2(p.x, p.y);
    a = VMod(a + an, 2.0 * an) - an;
    float l = length(p);
    p = l * float2(abs(sin(a)), cos(a));
    R = max(R - rounding, 1e-4);
    float2 A = float2(0.0, R);
    float2 B = R * k * float2(sin(an), cos(an));
    float2 ba = B - A;
    float2 pa = p - A;
    float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-12));
    float d = length(pa - ba * h);
    float cr = ba.x * pa.y - ba.y * pa.x;
    return (cr < 0.0 ? -d : d) - rounding;
}

float SdTriangle(float2 p, float2 p0, float2 p1, float2 p2)
{
    float2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
    float2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
    float2 pq0 = v0 - e0 * saturate(dot(v0, e0) / max(dot(e0, e0), 1e-12));
    float2 pq1 = v1 - e1 * saturate(dot(v1, e1) / max(dot(e1, e1), 1e-12));
    float2 pq2 = v2 - e2 * saturate(dot(v2, e2) / max(dot(e2, e2), 1e-12));
    float s = sign(e0.x * e2.y - e0.y * e2.x);
    float2 d = min(min(float2(dot(pq0, pq0), s * (v0.x * e0.y - v0.y * e0.x)),
                       float2(dot(pq1, pq1), s * (v1.x * e1.y - v1.y * e1.x))),
                       float2(dot(pq2, pq2), s * (v2.x * e2.y - v2.y * e2.x)));
    return -sqrt(d.x) * sign(d.y);
}

// cap: 0 = butt, 1 = round, 2 = square
float SdSegment(float2 p, float2 a, float2 b, float hw, uint cap)
{
    float2 ba = b - a;
    float l = length(ba);
    float2 d = l > 1e-6 ? ba / l : float2(1, 0);
    float2 q = p - (a + b) * 0.5;
    q = float2(dot(q, d), dot(q, float2(-d.y, d.x)));
    float hl = l * 0.5;
    if (cap == 1u)
    {
        q.x = max(abs(q.x) - hl, 0.0);
        return length(q) - hw;
    }
    if (cap == 2u) hl += hw;
    return SdBox(q, float2(hl, hw));
}

float SdCross(float2 p, float2 hs, float t, float r)
{
    float a = SdBox(p, float2(hs.x, t) - r) - r;
    float b = SdBox(p, float2(t, hs.y) - r) - r;
    return min(a, b);
}

// Heart normalized to y in [0, ~1.1]; caller scales.
float SdHeart(float2 p)
{
    p.x = abs(p.x);
    if (p.y + p.x > 1.0) return sqrt(Dot2(p - float2(0.25, 0.75))) - sqrt(2.0) / 4.0;
    return sqrt(min(Dot2(p - float2(0.00, 1.00)), Dot2(p - 0.5 * max(p.x + p.y, 0.0)))) * sign(p.x - p.y);
}

float SdRhombus(float2 p, float2 b)
{
    p = abs(p);
    float h = clamp(NDot(b - 2.0 * p, b) / dot(b, b), -1.0, 1.0);
    float d = length(p - 0.5 * b * float2(1.0 - h, 1.0 + h));
    return d * sign(p.x * b.y + p.y * b.x - b.x * b.y);
}

float SdTrapezoid(float2 p, float r1, float r2, float he)
{
    float2 k1 = float2(r2, he);
    float2 k2 = float2(r2 - r1, 2.0 * he);
    p.x = abs(p.x);
    float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
    float2 cb = p - k1 + k2 * saturate(dot(k1 - p, k2) / dot(k2, k2));
    float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
    return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
}

float SdParallelogram(float2 p, float wi, float he, float sk)
{
    float2 e = float2(sk, he);
    p = (p.y < 0.0) ? -p : p;
    float2 w = p - e;
    w.x -= clamp(w.x, -wi, wi);
    float2 d = float2(dot(w, w), -w.y);
    float s = p.x * e.y - p.y * e.x;
    p = (s < 0.0) ? -p : p;
    float2 v = p - float2(wi, 0.0);
    v -= e * clamp(dot(v, e) / dot(e, e), -1.0, 1.0);
    d = min(d, float2(dot(v, v), wi * he - abs(s)));
    return sqrt(d.x) * sign(-d.y);
}

float SdMoon(float2 p, float d, float ra, float rb)
{
    p.y = abs(p.y);
    d = max(d, 1e-5);
    float a = (ra * ra - rb * rb + d * d) / (2.0 * d);
    float b = sqrt(max(ra * ra - a * a, 0.0));
    if (d * (p.x * b - p.y * a) > d * d * max(b - p.y, 0.0)) return length(p - float2(a, b));
    return max(length(p) - ra, -(length(p - float2(d, 0.0)) - rb));
}

float SdEgg(float2 p, float ra, float rb)
{
    const float k = 1.7320508;
    p.x = abs(p.x);
    float r = ra - rb;
    return ((p.y < 0.0) ? length(p) - r :
            (k * (p.x + r) < p.y) ? length(float2(p.x, p.y - k * r)) :
            length(float2(p.x + r, p.y)) - 2.0 * r) - rb;
}

float SdVesica(float2 p, float r, float d)
{
    p = abs(p);
    float b = sqrt(max(r * r - d * d, 0.0));
    return ((p.y - b) * d > p.x * b) ? length(p - float2(0.0, b)) : length(p - float2(-d, 0.0)) - r;
}

float SdSquircle(float2 p, float2 hs, float n)
{
    float2 q = abs(p) / hs;
    float v = pow(pow(q.x, n) + pow(q.y, n), 1.0 / n);
    return (v - 1.0) * min(hs.x, hs.y);
}

float SdFlower(float2 p, float R, float petals, float amp, float rot)
{
    float a = atan2(p.y, p.x) - rot;
    float r = R * (1.0 - amp * 0.5 * (1.0 - cos(petals * a)));
    return (length(p) - r) * 0.75;
}

float SdArrow(float2 p, float halfLen, float halfHeadW, float headLen, float halfShaftW)
{
    headLen = min(headLen, 2.0 * halfLen);
    float shaftHalf = max(halfLen - headLen * 0.5, 0.0);
    float shaft = SdBox(p - float2(-headLen * 0.5, 0.0), float2(shaftHalf, halfShaftW));
    float2 tip = float2(halfLen, 0.0);
    float2 b0 = float2(halfLen - headLen, halfHeadW);
    float2 b1 = float2(halfLen - headLen, -halfHeadW);
    return min(shaft, SdTriangle(p, tip, b0, b1));
}

float SdChevron(float2 p, float2 hs, float ht)
{
    float2 a = float2(-hs.x + ht, hs.y - ht);
    float2 b = float2(hs.x - ht, 0.0);
    float2 c = float2(-hs.x + ht, -hs.y + ht);
    float2 pa = p - a, ba = b - a;
    float d1 = length(pa - ba * saturate(dot(pa, ba) / dot(ba, ba)));
    float2 pc = p - c, bc = b - c;
    float d2 = length(pc - bc * saturate(dot(pc, bc) / dot(bc, bc)));
    return min(d1, d2) - ht;
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

        float d = ShapeDistance(h.shape, h.style, p, i.s1, i.s2);
        d = ApplyModifier(h, d, p, i.hdr.y);
        float2 lp = i.s2.zw;
        color = EvaluatePaint(h, i.g, dgdx, dgdy, i.hdr.z, i.hdr.w, i.color);

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
            float2 cell = floor(p / (px * max(lp.y, 1.0)));
            float n = Hash21(cell) * 2.0 - 1.0;
            alpha = inside * abs(n) * lp.x;
            color = n >= 0.0 ? float4(1.0, 1.0, 1.0, color.a) : float4(0.0, 0.0, 0.0, color.a);
        }

        if (h.modifier == MOD_DISSOLVE)
        {
            float3 dv = Unpack8(i.hdr.y);
            float n = ValueNoise(p / max(exp2(dv.z * 16.0 - 4.0), 1e-4));
            alpha *= smoothstep(dv.x - dv.y * 0.5, dv.x + dv.y * 0.5 + 1e-4, n);
        }
    }

    // Gradients are dithered by +-0.5 of an 8-bit step to hide banding.
    if ((h.paint >= PAINT_LINEAR && h.paint <= PAINT_FOCAL) || h.paint == PAINT_FREEFORM)
        color.rgb += (Hash21(floor(i.p / px)) - 0.5) * (1.0 / 255.0);

    color.a *= alpha;
    color.rgb *= color.a;
    if (h.additive) color.a = 0.0;
    return color;
}

#endif
