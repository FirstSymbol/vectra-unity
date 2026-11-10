Shader "Vectra/World URP"
{
    // The world shader for the Universal Render Pipeline. Same output as "Vectra/World", written against the URP shader library
    // so every property sits in UnityPerMaterial and the SRP Batcher can batch all Vectra shapes that share a material.
    // Per-shape data is in the vertices, so shapes need no MaterialPropertyBlock.
    Properties
    {
        _MainTex ("Paint Texture", 2D) = "white" {}
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
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Cull [_Cull]
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        HLSLINCLUDE
        #pragma target 3.5
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_instancing
        #pragma multi_compile_fog

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _Color;
            float _SrcBlend;
            float _DstBlend;
            float _Cull;
            float _ZTest;
            float _ZWrite;
        CBUFFER_END
        #define VECTRA_MATERIAL_DECLARED

        #include "../../../Runtime/Resources/Vectra/VectraCore.hlsl"

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float4 color : COLOR;
            float4 pg : TEXCOORD0;
            nointerpolation float4 hdr : TEXCOORD1;
            nointerpolation float4 s1 : TEXCOORD2;
            nointerpolation float4 s2 : TEXCOORD3;
            float fogFactor : TEXCOORD4;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert(VectraAttributes v)
        {
            v2f o = (v2f)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.vertex = TransformObjectToHClip(v.vertex.xyz);
            o.color = v.color * _Color;
            o.pg = float4(v.uv0.xy, v.uv3.xy);
            o.hdr = float4(v.uv0.zw, v.uv3.zw);
            o.s1 = v.uv1;
            o.s2 = v.uv2;
            o.fogFactor = ComputeFogFactor(o.vertex.z);
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            VectraFragmentInput f;
            f.p = i.pg.xy;
            f.g = i.pg.zw;
            f.hdr = i.hdr;
            f.s1 = i.s1;
            f.s2 = i.s2;
            f.color = i.color;
            float4 color = VectraShade(f);
            // Premultiplied output: fog colour is scaled by alpha.
            color.rgb = MixFogColor(color.rgb, unity_FogColor.rgb * color.a, i.fogFactor);
            return color;
        }
        ENDHLSL

        Pass
        {
            Name "Vectra"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            ENDHLSL
        }

        // The 2D Renderer draws shapes with this pass.
        Pass
        {
            Name "Vectra2D"
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            ENDHLSL
        }
    }

    Fallback Off
}
