// Draws a picture of an SVG (<image>) in a uGUI Canvas: UI/Default plus crisp texels when the picture is magnified (what Figma draws).
// Each texel is a sharp block whose edge is anti-aliased over about one screen pixel; minified or at 1:1 it samples as usual.
Shader "Vectra/Image"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

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
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;

            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                float4 c = v.color;
                if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
                    c.rgb = UIGammaToLinear(c.rgb);
                o.color = c * _Color;
                return o;
            }

            // Same function as SharpTexelUV in VectraCore.hlsl.
            float2 SharpTexelUV(float2 uv, float2 dgdx, float2 dgdy)
            {
                float2 size = _MainTex_TexelSize.zw;
                float2 t = uv * size;
                float2 w = clamp(max(abs(dgdx), abs(dgdy)) * size, 1e-4, 1.0);
                float2 fi = floor(t);
                float2 s = t - fi - 0.5;
                float2 o = sign(s) * 0.5 * saturate((abs(s) - (0.5 - 0.5 * w)) / (0.5 * w));
                return (fi + 0.5 + o) / size;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 dx = ddx(i.texcoord), dy = ddy(i.texcoord);
                half4 c = tex2Dgrad(_MainTex, SharpTexelUV(i.texcoord, dx, dy), dx, dy) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
