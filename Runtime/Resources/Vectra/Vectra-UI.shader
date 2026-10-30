Shader "Vectra/UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Paint Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

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
        Blend [_SrcBlend] [_DstBlend]
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "VectraCore.hlsl"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local _ VECTRA_SOFT_MASK

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 pg : TEXCOORD0;
                nointerpolation float4 hdr : TEXCOORD1;
                nointerpolation float4 s1 : TEXCOORD2;
                nointerpolation float4 s2 : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
                float4 mask : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;
            int _UIVertexColorAlwaysGammaSpace;

            #ifdef VECTRA_SOFT_MASK
            // Soft mask (VectraMask): canvas space -> mask shape space, the mask's distance function and
            // params = (softness, inset, invert, kind | style << 5).
            float4x4 _VectraMaskMatrix;
            float4 _VectraMaskS1;
            float4 _VectraMaskS2;
            float4 _VectraMaskParams;

            float VectraSoftMask(float2 canvasPos)
            {
                float2 q = mul(_VectraMaskMatrix, float4(canvasPos, 0.0, 1.0)).xy;
                float2 dqdx = ddx(q), dqdy = ddy(q);
                float px = max(sqrt(abs(dqdx.x * dqdy.y - dqdx.y * dqdy.x)), 1e-7);
                uint code = (uint)round(_VectraMaskParams.w);
                float d = ShapeDistance(code & 31u, code >> 5, q, _VectraMaskS1, _VectraMaskS2) + _VectraMaskParams.y;
                // The feather runs from the edge inwards; below a pixel it is plain anti-aliasing.
                float soft = _VectraMaskParams.x;
                float m = soft > px ? smoothstep(0.0, soft, -d) : Coverage(d, 0.0, px);
                return _VectraMaskParams.z > 0.5 ? 1.0 - m : m;
            }
            #endif

            v2f vert(VectraAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 vPosition = UnityObjectToClipPos(v.vertex);
                o.worldPosition = v.vertex;
                o.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                o.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                float4 c = v.color;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    c.rgb = UIGammaToLinear(c.rgb);
                o.color = c * _Color;
                o.pg = float4(v.uv0.xy, v.uv3.xy);
                o.hdr = float4(v.uv0.zw, v.uv3.zw);
                o.s1 = v.uv1;
                o.s2 = v.uv2;
                return o;
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
                float4 color = VectraShade(f);

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.mask.xy)) * i.mask.zw);
                color *= m.x * m.y;
                #endif

                #ifdef VECTRA_SOFT_MASK
                color *= VectraSoftMask(i.worldPosition.xy);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
