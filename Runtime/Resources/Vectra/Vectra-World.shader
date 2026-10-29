Shader "Vectra/World"
{
    Properties
    {
        [PerRendererData] _MainTex ("Paint Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test", Float) = 4
        [Toggle] _ZWrite ("Z Write", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull [_Cull]
        Lighting Off
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "Vectra"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "UnityCG.cginc"
            #include "VectraCore.hlsl"

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
                float4 pg : TEXCOORD0;
                nointerpolation float4 hdr : TEXCOORD1;
                nointerpolation float4 s1 : TEXCOORD2;
                nointerpolation float4 s2 : TEXCOORD3;
                UNITY_FOG_COORDS(4)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _Color;

            v2f vert(VectraAttributes v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.pg = float4(v.uv0.xy, v.uv3.xy);
                o.hdr = float4(v.uv0.zw, v.uv3.zw);
                o.s1 = v.uv1;
                o.s2 = v.uv2;
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                VectraFragmentInput f;
                f.p = i.pg.xy;
                f.g = i.pg.zw;
                f.hdr = i.hdr;
                f.s1 = i.s1;
                f.s2 = i.s2;
                f.color = i.color;
                float4 color = VectraShade(f);
                // Premultiplied output: fog colour is scaled by alpha.
                float4 fogColor = unity_FogColor * color.a;
                UNITY_APPLY_FOG_COLOR(i.fogCoord, color, fogColor);
                return color;
            }
            ENDCG
        }
    }

    Fallback Off
}
