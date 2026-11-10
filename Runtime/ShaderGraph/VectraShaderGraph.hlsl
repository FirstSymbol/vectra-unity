// Vectra - functions for Shader Graph Custom Function nodes (File mode). They give a graph the same distance fields,
// anti-aliasing and paints as Vectra's own shaders. Fragment stage only (they use screen-space derivatives).
// The Sub Graphs "Vectra SDF Shape", "Vectra Coverage", "Vectra Outline" and "Vectra Paint" wrap them.

#ifndef VECTRA_SHADER_GRAPH_INCLUDED
#define VECTRA_SHADER_GRAPH_INCLUDED

#include "../Resources/Vectra/VectraSdf.hlsl"

float VectraSgHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float VectraSgNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = VectraSgHash21(i), b = VectraSgHash21(i + float2(1, 0));
    float c = VectraSgHash21(i + float2(0, 1)), d = VectraSgHash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Position of a point of the 0..1 square UV in shape space: centred on the origin, Size units across.
void VectraCentered_float(float2 UV, float2 Size, out float2 Position)
{
    Position = (UV - 0.5) * Size;
}

void VectraCentered_half(half2 UV, half2 Size, out half2 Position)
{
    Position = (UV - 0.5) * Size;
}

// Signed distance (negative inside) of a shape centred on the origin, in the units of Position.
// Shape: 0 rectangle, 1 ellipse, 2 star, 3 regular polygon, 4 cross, 5 rhombus, 6 squircle, 7 capsule.
// Size: full width and height. Roundness: corner radius (rectangle) or corner rounding (star, polygon, cross).
// Points: number of points or sides (star, polygon) or the exponent (squircle, default 4).
void VectraSdfShape_float(float2 Position, float Shape, float2 Size, float Roundness, float Points, out float Distance)
{
    float2 hs = max(abs(Size) * 0.5, 1e-5);
    float m = min(hs.x, hs.y);
    float r = clamp(Roundness, 0.0, m);
    int kind = (int)round(Shape);
    float d;
    if (kind == 1) d = SdEllipse(Position, hs);
    else if (kind == 2) d = SdStar(Position, m, max(Points, 3.0), 0.5, 0.0, r);
    else if (kind == 3) d = SdStar(Position, m, max(Points, 3.0), cos(VECTRA_PI / max(Points, 3.0)), 0.0, r);
    else if (kind == 4) d = SdCross(Position, hs, m * 0.35, r);
    else if (kind == 5) d = SdRhombus(Position, hs);
    else if (kind == 6) d = SdSquircle(Position, hs, Points < 2.0 ? 4.0 : Points);
    else if (kind == 7)
    {
        float2 q = float2(max(abs(Position.x) - (hs.x - hs.y), 0.0), Position.y);
        d = length(q) - hs.y;
    }
    else d = SdRect(Position, hs, float4(r, r, r, r), 0u, 0.6);
    Distance = d;
}

void VectraSdfShape_half(half2 Position, half Shape, half2 Size, half Roundness, half Points, out half Distance)
{
    float d;
    VectraSdfShape_float(Position, Shape, Size, Roundness, Points, d);
    Distance = d;
}

// Size of one screen pixel in the units of Distance (measured from its derivatives).
void VectraPixelSize_float(float Distance, out float PixelSize)
{
    PixelSize = max(length(float2(ddx(Distance), ddy(Distance))), 1e-7);
}

void VectraPixelSize_half(half Distance, out half PixelSize)
{
    PixelSize = max(length(float2(ddx(Distance), ddy(Distance))), 1e-7);
}

// Opacity of a distance field with Vectra's anti-aliasing. Softness blurs the edge, in the units of Distance (0 = crisp).
void VectraCoverage_float(float Distance, float Softness, out float Alpha)
{
    float px = max(length(float2(ddx(Distance), ddy(Distance))), 1e-7);
    Alpha = Coverage(Distance, Softness, px);
}

void VectraCoverage_half(half Distance, half Softness, out half Alpha)
{
    float px = max(length(float2(ddx(Distance), ddy(Distance))), 1e-7);
    Alpha = Coverage(Distance, Softness, px);
}

// Opacity of an outline around the edge of a distance field.
// Alignment: 0 centred on the edge, 1 inside, 2 outside. Pixels: 0 = Width is in the units of Distance, 1 = Width is in screen pixels
// (a hairline that keeps its width at any scale).
void VectraOutline_float(float Distance, float Width, float Alignment, float Pixels, out float Alpha)
{
    float px = max(length(float2(ddx(Distance), ddy(Distance))), 1e-7);
    float w = Pixels > 0.5 ? Width * px : Width;
    float hw = w * 0.5;
    int a = (int)round(Alignment);
    float offset = a == 1 ? -hw : (a == 2 ? hw : 0.0);
    Alpha = Coverage(abs(Distance - offset) - hw, 0.0, px);
}

void VectraOutline_half(half Distance, half Width, half Alignment, half Pixels, out half Alpha)
{
    float a;
    VectraOutline_float(Distance, Width, Alignment, Pixels, a);
    Alpha = a;
}

// Paint at UV (0..1 across the shape). Kind: 0 solid, 1 linear, 2 radial, 3 angular, 4 diamond, 5 stripes, 6 checker, 7 dots,
// 8 grid, 9 noise. Angle in degrees rotates the paint. Scale is the number of repeats of the patterns.
// The result is not premultiplied.
void VectraPaint_float(float2 UV, float Kind, float4 ColorA, float4 ColorB, float Angle, float Scale, out float4 Color)
{
    float2 p = Rotate(UV - 0.5, radians(Angle));
    float2 ddp = float2(length(float2(ddx(p.x), ddy(p.x))), length(float2(ddx(p.y), ddy(p.y))));
    float aa = max(max(ddp.x, ddp.y) * Scale, 1e-4);
    int kind = (int)round(Kind);
    float t = 0.0;
    if (kind == 1) t = saturate(p.x + 0.5);
    else if (kind == 2) t = saturate(length(p) * 2.0);
    else if (kind == 3) t = frac(atan2(p.y, p.x) / VECTRA_TAU + 0.5);
    else if (kind == 4) t = saturate((abs(p.x) + abs(p.y)) * 2.0);
    else if (kind == 5)
    {
        float f = frac(p.x * Scale);
        t = smoothstep(-aa, aa, 0.25 - abs(f - 0.5));
    }
    else if (kind == 6)
    {
        float2 c = floor(p * Scale);
        t = fmod(abs(c.x + c.y), 2.0);
    }
    else if (kind == 7)
    {
        float2 f = frac(p * Scale) - 0.5;
        t = 1.0 - smoothstep(0.3 - aa, 0.3 + aa, length(f));
    }
    else if (kind == 8)
    {
        float2 f = abs(frac(p * Scale) - 0.5);
        float edge = 0.5 - max(f.x, f.y);
        t = 1.0 - smoothstep(0.02, 0.02 + aa, edge);
    }
    else if (kind == 9) t = VectraSgNoise(p * Scale);
    Color = kind == 0 ? ColorA : lerp(ColorA, ColorB, t);
}

void VectraPaint_half(half2 UV, half Kind, half4 ColorA, half4 ColorB, half Angle, half Scale, out half4 Color)
{
    float4 c;
    VectraPaint_float(UV, Kind, ColorA, ColorB, Angle, Scale, c);
    Color = c;
}

// Puts an outline over a fill: both opacities are coverage values (for example from VectraCoverage and VectraOutline).
// Color is not premultiplied; Alpha is the combined opacity.
void VectraComposeOutline_float(float4 Fill, float FillAlpha, float4 OutlineColor, float OutlineAlpha, out float3 Color, out float Alpha)
{
    float fa = Fill.a * FillAlpha;
    float oa = OutlineColor.a * OutlineAlpha;
    Alpha = oa + fa * (1.0 - oa);
    Color = Alpha > 1e-5 ? (OutlineColor.rgb * oa + Fill.rgb * fa * (1.0 - oa)) / Alpha : Fill.rgb;
}

void VectraComposeOutline_half(half4 Fill, half FillAlpha, half4 OutlineColor, half OutlineAlpha, out half3 Color, out half Alpha)
{
    float3 c;
    float a;
    VectraComposeOutline_float(Fill, FillAlpha, OutlineColor, OutlineAlpha, c, a);
    Color = c;
    Alpha = a;
}

#endif
