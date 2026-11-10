// Compositing of offscreen layers (premultiplied alpha everywhere). Used with Graphics.Blit by SvgCompositor.
// Pass 0: layer over the target (opacity). Pass 1: layer with a blend mode, reading a copy of the target as the backdrop.
// Pass 2: multiplies the target by a mask. Pass 3: one direction of a Gaussian blur.
Shader "Hidden/Vectra/Composite"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BackTex ("Backdrop", 2D) = "black" {}
        _Opacity ("Opacity", Float) = 1
        _Mode ("Blend mode", Float) = 0
        _MaskKind ("Mask kind (0 alpha, 1 luminance)", Float) = 0
        _Blur ("Blur (step uv x, step uv y, sigma in taps, radius in taps)", Vector) = (0,0,1,3)
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _BackTex;
    float _Opacity;
    float _Mode;
    float _MaskKind;
    float4 _Blur;

    float4 FragOver(v2f_img i) : SV_Target
    {
        return tex2D(_MainTex, i.uv) * _Opacity;
    }

    float4 FragMask(v2f_img i) : SV_Target
    {
        float4 m = tex2D(_MainTex, i.uv);
        // Premultiplied colour: its luminance already includes the alpha.
        float v = _MaskKind > 0.5 ? saturate(dot(m.rgb, float3(0.2125, 0.7154, 0.0721))) : m.a;
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
            sum += tex2Dlod(_MainTex, float4(i.uv + _Blur.xy * k, 0, 0)) * w;
            wsum += w;
        }

        return sum / wsum;
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
        if (mode == 16) return min(s + b, 1.0);
        float ar = s.a + b.a - s.a * b.a;
        float3 cs = s.a > 1e-5 ? s.rgb / s.a : float3(0, 0, 0);
        float3 cb = b.a > 1e-5 ? b.rgb / b.a : float3(0, 0, 0);
        float3 mixed = BlendColors(mode, saturate(cb), saturate(cs));
        float3 rgb = (1.0 - b.a) * s.rgb + (1.0 - s.a) * b.rgb + s.a * b.a * mixed;
        return float4(rgb, ar);
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
    }
}
