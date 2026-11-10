// Compositing of offscreen layers (premultiplied alpha everywhere). Used with Graphics.Blit by SvgCompositor.
// Pass 0: layer over the target (opacity). Pass 1: layer with a blend mode, reading a copy of the target as the backdrop.
// Pass 2: multiplies the target by a mask. Pass 3: one direction of a Gaussian blur. Pass 4: gamma picture to linear light.
// Filter steps (shadows of a layer): 5 one direction of a blur of the alpha channel with an offset, 6 morphology (spread, choke) of the alpha,
// 7 paints an alpha picture with the shadow colour, 9 the inner shadow (layer alpha minus the blurred alpha), 8 and 10 the background blur
// of what lies inside a shape (weighting by the shape, then division by the blurred shape).
// Passes are called by index: add new ones at the end.
Shader "Hidden/Vectra/Composite"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BackTex ("Backdrop", 2D) = "black" {}
        _Opacity ("Opacity", Float) = 1
        _Mode ("Blend mode", Float) = 0
        _MaskKind ("Mask kind (0 alpha, 1 luminance)", Float) = 0
        _Linear ("Linear colour space (textures are sRGB, maths on sRGB values)", Float) = 0
        _Blur ("Blur (step uv x, step uv y, sigma in taps, radius in taps)", Vector) = (0,0,1,3)
        _Offset ("Shadow offset in uv (the source is read at uv - offset)", Vector) = (0,0,0,0)
        _Morph ("Morphology (step uv x, step uv y, radius in pixels, 1 dilate / 0 erode)", Vector) = (0,0,0,1)
        _Color ("Shadow colour (straight, sRGB values; a Vector so that Unity does not convert it)", Vector) = (0,0,0,1)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _BackTex;
    float _Opacity;
    float _Mode;
    float _MaskKind;
    float _Linear;

    // In a Linear project the textures are sRGB and read as linear values; browsers and Figma blend and mask on the sRGB-encoded values.
    float3 ToBlendSpace(float3 c) { return _Linear > 0.5 ? LinearToGammaSpace(c) : c; }
    float3 FromBlendSpace(float3 c) { return _Linear > 0.5 ? GammaToLinearSpace(c) : c; }
    float4 _Blur;

    float4 FragOver(v2f_img i) : SV_Target
    {
        return tex2D(_MainTex, i.uv) * _Opacity;
    }

    float4 FragMask(v2f_img i) : SV_Target
    {
        float4 m = tex2D(_MainTex, i.uv);
        // Premultiplied colour: its luminance already includes the alpha.
        float3 straight = m.a > 1e-5 ? ToBlendSpace(saturate(m.rgb / m.a)) : float3(0, 0, 0);
        float v = _MaskKind > 0.5 ? saturate(dot(straight, float3(0.2125, 0.7154, 0.0721)) * m.a) : m.a;
        return float4(v, v, v, v);
    }

    float4 FragBlur(v2f_img i) : SV_Target
    {
        int r = (int)_Blur.w;
        float inv = -0.5 / max(_Blur.z * _Blur.z, 1e-4);
        float4 sum = 0;
        float wsum = 0;
        [loop] for (int k = -r; k <= r; k++)
        {
            float w = exp(k * k * inv);
            float4 c = tex2Dlod(_MainTex, float4(i.uv + _Blur.xy * k, 0, 0));
            c.rgb = ToBlendSpace(c.rgb);
            sum += c * w;
            wsum += w;
        }

        float4 res = sum / wsum;
        res.rgb = FromBlendSpace(res.rgb);
        return res;
    }

    float4 _Offset;
    float4 _Morph;
    float4 _Color;

    // Filter steps work on the alpha channel only (the other channels stay 0): a one-direction Gaussian blur that also shifts the picture.
    float4 FragAlphaBlur(v2f_img i) : SV_Target
    {
        int r = (int)_Blur.w;
        float inv = -0.5 / max(_Blur.z * _Blur.z, 1e-4);
        float sum = 0;
        float wsum = 0;
        [loop] for (int k = -r; k <= r; k++)
        {
            float w = exp(k * k * inv);
            sum += tex2Dlod(_MainTex, float4(i.uv - _Offset.xy + _Blur.xy * k, 0, 0)).a * w;
            wsum += w;
        }

        return float4(0, 0, 0, sum / wsum);
    }

    // feMorphology on the alpha channel along one axis: a rectangle of the given radius in pixels, the last partial pixel weighted.
    float4 FragMorph(v2f_img i) : SV_Target
    {
        float radius = _Morph.z;
        int r = (int)ceil(radius);
        bool dilate = _Morph.w > 0.5;
        float res = dilate ? 0.0 : 1.0;
        [loop] for (int k = -r; k <= r; k++)
        {
            float a = tex2Dlod(_MainTex, float4(i.uv + _Morph.xy * k, 0, 0)).a;
            float w = saturate(radius - abs((float)k) + 1.0);
            res = dilate ? max(res, a * w) : min(res, 1.0 - w * (1.0 - a));
        }

        return float4(0, 0, 0, res);
    }

    float4 TintAlpha(float a)
    {
        float3 c = FromBlendSpace(_Color.rgb);
        return float4(c * _Color.a * a, _Color.a * a);
    }

    // A shadow: the alpha picture (source) painted with the colour.
    float4 FragTint(v2f_img i) : SV_Target
    {
        return TintAlpha(tex2D(_MainTex, i.uv).a);
    }

    // An inner shadow: where the layer is and the blurred, shifted inverse of it is not (_MainTex = layer picture, _BackTex = the blurred alpha).
    float4 FragInner(v2f_img i) : SV_Target
    {
        return TintAlpha(saturate(tex2D(_MainTex, i.uv).a - tex2D(_BackTex, i.uv).a));
    }

    // The picture weighted by a region (_BackTex alpha), the weighting done on blend-space colours so that the blur (which averages them) sums them.
    float4 FragMasked(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float m = tex2D(_BackTex, i.uv).a;
        return float4(FromBlendSpace(ToBlendSpace(c.rgb) * m), c.a * m);
    }

    // The blur of the weighted picture divided by the blur of the region itself (_BackTex alpha): the blur of what is inside only.
    float4 FragDivide(v2f_img i) : SV_Target
    {
        float4 s = tex2D(_MainTex, i.uv);
        float den = max(tex2D(_BackTex, i.uv).a, 1e-3);
        return float4(FromBlendSpace(saturate(ToBlendSpace(s.rgb) / den)), saturate(s.a / den));
    }

    // Gamma picture (premultiplied sRGB values) into linear light, premultiplied, for an sRGB target.
    float4 FragDecode(v2f_img i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float3 straight = c.a > 1e-5 ? saturate(c.rgb / c.a) : float3(0, 0, 0);
        return float4(GammaToLinearSpace(straight) * c.a, c.a);
    }

    // ---- blend modes: Compositing and Blending Level 1 ----

    float Lum(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }

    float3 ClipColor(float3 c)
    {
        float l = Lum(c);
        float n = min(min(c.r, c.g), c.b);
        float x = max(max(c.r, c.g), c.b);
        if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-6);
        if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-6);
        return c;
    }

    float3 SetLum(float3 c, float l) { return ClipColor(c + (l - Lum(c))); }

    float Sat(float3 c) { return max(max(c.r, c.g), c.b) - min(min(c.r, c.g), c.b); }

    float3 SetSat(float3 c, float s)
    {
        float mx = max(max(c.r, c.g), c.b);
        float mn = min(min(c.r, c.g), c.b);
        return mx > mn ? (c - mn) * s / (mx - mn) : float3(0, 0, 0);
    }

    float HardLight1(float cb, float cs)
    {
        return cs <= 0.5 ? cb * 2.0 * cs : cb + (2.0 * cs - 1.0) - cb * (2.0 * cs - 1.0);
    }

    float SoftLight1(float cb, float cs)
    {
        if (cs <= 0.5) return cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb);
        float d = cb <= 0.25 ? ((16.0 * cb - 12.0) * cb + 4.0) * cb : sqrt(cb);
        return cb + (2.0 * cs - 1.0) * (d - cb);
    }

    float Separable(int mode, float cb, float cs)
    {
        if (mode == 1) return cb * cs;
        if (mode == 2) return cb + cs - cb * cs;
        if (mode == 3) return HardLight1(cs, cb);
        if (mode == 4) return min(cb, cs);
        if (mode == 5) return max(cb, cs);
        if (mode == 6) return cb <= 0.0 ? 0.0 : (cs >= 1.0 ? 1.0 : min(1.0, cb / (1.0 - cs)));
        if (mode == 7) return cb >= 1.0 ? 1.0 : (cs <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - cb) / cs));
        if (mode == 8) return HardLight1(cb, cs);
        if (mode == 9) return SoftLight1(cb, cs);
        if (mode == 10) return abs(cb - cs);
        if (mode == 11) return cb + cs - 2.0 * cb * cs;
        return cs;
    }

    float3 BlendColors(int mode, float3 cb, float3 cs)
    {
        if (mode == 12) return SetLum(SetSat(cs, Sat(cb)), Lum(cb));
        if (mode == 13) return SetLum(SetSat(cb, Sat(cs)), Lum(cb));
        if (mode == 14) return SetLum(cs, Lum(cb));
        if (mode == 15) return SetLum(cb, Lum(cs));
        return float3(Separable(mode, cb.r, cs.r), Separable(mode, cb.g, cs.g), Separable(mode, cb.b, cs.b));
    }

    float4 FragBlend(v2f_img i) : SV_Target
    {
        float4 s = tex2D(_MainTex, i.uv) * _Opacity;
        float4 b = tex2D(_BackTex, i.uv);
        int mode = (int)(_Mode + 0.5);
        // In a Linear project the whole composite runs on sRGB-encoded values, like browsers and Figma, then goes back to linear.
        float3 cs = s.a > 1e-5 ? ToBlendSpace(saturate(s.rgb / s.a)) : float3(0, 0, 0);
        float3 cb = b.a > 1e-5 ? ToBlendSpace(saturate(b.rgb / b.a)) : float3(0, 0, 0);
        float4 r;
        if (mode == 16)
        {
            r = min(float4(cs * s.a, s.a) + float4(cb * b.a, b.a), 1.0);
        }
        else
        {
            float ar = s.a + b.a - s.a * b.a;
            float3 mixed = BlendColors(mode, cb, cs);
            float3 rgb = (1.0 - b.a) * cs * s.a + (1.0 - s.a) * cb * b.a + s.a * b.a * mixed;
            r = float4(rgb, ar);
        }

        float3 straight = r.a > 1e-5 ? saturate(r.rgb / r.a) : float3(0, 0, 0);
        return float4(FromBlendSpace(straight) * r.a, r.a);
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Over"
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragOver
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Blend"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBlend
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Mask"
            Blend Zero SrcColor
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragMask
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Blur"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragBlur
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Decode"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragDecode
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "AlphaBlur"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragAlphaBlur
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Morph"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragMorph
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Tint"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragTint
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Divide"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragDivide
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Inner"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragInner
            #pragma target 3.5
            ENDCG
        }

        Pass
        {
            Name "Masked"
            Blend Off
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment FragMasked
            #pragma target 3.5
            ENDCG
        }
    }
}
