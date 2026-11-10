Shader "Vectra/World HDRP"
{
    // The world shader for the High Definition Render Pipeline: an unlit transparent pass with every property in UnityPerMaterial,
    // so the SRP Batcher can batch Vectra shapes. Per-shape data is in the vertices, so shapes need no MaterialPropertyBlock.
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
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            v2f vert(VectraAttributes v)
            {
                v2f o = (v2f)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                // HDRP works in camera-relative space.
                float3 positionRWS = TransformObjectToWorld(v.vertex.xyz);
                o.vertex = TransformWorldToHClip(positionRWS);
                o.color = v.color * _Color;
                o.pg = float4(v.uv0.xy, v.uv3.xy);
                o.hdr = float4(v.uv0.zw, v.uv3.zw);
                o.s1 = v.uv1;
                o.s2 = v.uv2;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                VectraFragmentInput f;
                f.p = i.pg.xy;
                f.g = i.pg.zw;
                f.hdr = i.hdr;
                f.s1 = i.s1;
                f.s2 = i.s2;
                f.color = i.color;
                float4 color = VectraShade(f);
                // Unlit colours are not exposed: undo the exposure the camera applies afterwards.
                color.rgb *= _DeExposureMultiplier;
                return color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
