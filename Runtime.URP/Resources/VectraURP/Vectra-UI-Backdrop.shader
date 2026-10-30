Shader "Vectra/UI Backdrop"
{
    // Vectra UI shapes composited with what is behind them, using the textures published by
    // VectraBackdropFeature: frosted glass (blurred backdrop + tint) or advanced blend modes.
    Properties
    {
        [PerRendererData] _MainTex ("Paint Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BackdropMode ("Backdrop Mode", Float) = 0
        _TintStrength ("Tint Strength", Range(0, 1)) = 0.25
        _Saturation ("Backdrop Saturation", Range(0, 2)) = 1.2
        _Brightness ("Backdrop Brightness", Range(-1, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "../../../Runtime/Resources/Vectra/VectraCore.hlsl"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            sampler2D _VectraBackdropBlur;
            sampler2D _VectraBackdropSharp;
            fixed4 _Color;
            float4 _ClipRect;
            float _BackdropMode, _TintStrength, _Saturation, _Brightness;

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 pg : TEXCOORD0;
                nointerpolation float4 hdr : TEXCOORD1;
                nointerpolation float4 s1 : TEXCOORD2;
                nointerpolation float4 s2 : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
                float4 screen : TEXCOORD5;
            };

            v2f vert(VectraAttributes v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPosition = v.vertex;
                o.screen = ComputeScreenPos(o.vertex);
                o.color = v.color * _Color;
                o.pg = float4(v.uv0.xy, v.uv3.xy);
                o.hdr = float4(v.uv0.zw, v.uv3.zw);
                o.s1 = v.uv1;
                o.s2 = v.uv2;
                return o;
            }

            float Lum(float3 c) { return dot(c, float3(0.3, 0.59, 0.11)); }

            float3 ClipColor(float3 c)
            {
                float l = Lum(c), n = min(c.r, min(c.g, c.b)), x = max(c.r, max(c.g, c.b));
                if (n < 0.0) c = l + (c - l) * l / max(l - n, 1e-5);
                if (x > 1.0) c = l + (c - l) * (1.0 - l) / max(x - l, 1e-5);
                return c;
            }

            float3 SetLum(float3 c, float l) { return ClipColor(c + (l - Lum(c))); }
            float Sat(float3 c) { return max(c.r, max(c.g, c.b)) - min(c.r, min(c.g, c.b)); }

            float3 SetSat(float3 c, float s)
            {
                float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                return mx > mn ? (c - mn) * s / (mx - mn) : 0.0;
            }

            float3 SoftLight(float3 b, float3 s)
            {
                float3 d = b <= 0.25 ? ((16.0 * b - 12.0) * b + 4.0) * b : sqrt(b);
                return s <= 0.5 ? b - (1.0 - 2.0 * s) * b * (1.0 - b) : b + (2.0 * s - 1.0) * (d - b);
            }

            // b = backdrop, s = source (W3C compositing formulas)
            float3 BlendMode(int mode, float3 b, float3 s)
            {
                if (mode == 1) return b <= 0.5 ? 2.0 * b * s : 1.0 - 2.0 * (1.0 - b) * (1.0 - s);   // overlay
                if (mode == 2) return SoftLight(b, s);
                if (mode == 3) return s <= 0.5 ? 2.0 * b * s : 1.0 - 2.0 * (1.0 - b) * (1.0 - s);   // hard light
                if (mode == 4) return s >= 1.0 ? 1.0 : min(1.0, b / max(1.0 - s, 1e-5));              // color dodge
                if (mode == 5) return s <= 0.0 ? 0.0 : 1.0 - min(1.0, (1.0 - b) / max(s, 1e-5));      // color burn
                if (mode == 6) return abs(b - s);                                                       // difference
                if (mode == 7) return b + s - 2.0 * b * s;                                              // exclusion
                if (mode == 8) return SetLum(SetSat(s, Sat(b)), Lum(b));                                // hue
                if (mode == 9) return SetLum(SetSat(b, Sat(s)), Lum(b));                                // saturation
                if (mode == 10) return SetLum(s, Lum(b));                                               // color
                if (mode == 11) return SetLum(b, Lum(s));                                               // luminosity
                if (mode == 12) return min(b, s);                                                       // darken
                if (mode == 13) return max(b, s);                                                       // lighten
                if (mode == 14) return max(b + s - 1.0, 0.0);                                           // linear burn
                if (mode == 15) return min(b + s, 1.0);                                                 // linear dodge
                return s;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                VectraFragmentInput f;
                f.p = i.pg.xy;
                f.g = i.pg.zw;
                f.hdr = i.hdr;
                f.s1 = i.s1;
                f.s2 = i.s2;
                f.color = i.color;
                float4 c = VectraShade(f);
                float a = c.a;
                float3 src = a > 1e-5 ? c.rgb / a : 0.0;
                float2 uv = i.screen.xy / i.screen.w;
                int mode = (int)round(_BackdropMode);
                float3 result;
                if (mode == 0)
                {
                    // Frosted glass: blurred backdrop, adjusted, tinted by the paint.
                    float3 blur = tex2D(_VectraBackdropBlur, uv).rgb;
                    float l = Lum(blur);
                    blur = lerp(l.xxx, blur, _Saturation) + _Brightness;
                    result = lerp(blur, src, _TintStrength);
                }
                else
                {
                    float3 b = tex2D(_VectraBackdropSharp, uv).rgb;
                    result = BlendMode(mode, b, src);
                }

                float4 o = float4(result * a, a);
                #ifdef UNITY_UI_CLIP_RECT
                o *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(o.a - 0.001);
                #endif
                return o;
            }
            ENDCG
        }
    }

    Fallback "Vectra/UI"
}
