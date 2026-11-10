// Vectra - distance functions and anti-aliasing coverage, shared by VectraCore.hlsl and the Shader Graph nodes.
// No textures, no properties and no engine includes, so it can be included anywhere.

#ifndef VECTRA_SDF_INCLUDED
#define VECTRA_SDF_INCLUDED

#define VECTRA_PI 3.14159265359
#define VECTRA_TAU 6.28318530718

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
    // An edge without blur is a box-filtered pixel coverage: linear over one pixel, so an edge on a pixel boundary stays crisp.
    if (sigma <= px * 0.42) return saturate(0.5 - d / max(px, 1e-12));
    float s = sigma;
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

#endif
