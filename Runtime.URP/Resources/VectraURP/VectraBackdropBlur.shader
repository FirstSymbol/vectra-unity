Shader "Hidden/Vectra/BackdropBlur"
{
    // Pass 0: 4-tap downsample. Passes 1-4: Kawase blur with increasing offsets.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 Kawase(Varyings i, float offset)
        {
            float2 t = _BlitTexture_TexelSize.xy * (offset + 0.5);
            float2 uv = i.texcoord;
            float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(t.x, t.y), 0);
            c += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, t.y), 0);
            c += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(t.x, -t.y), 0);
            c += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(-t.x, -t.y), 0);
            return c * 0.25;
        }
        ENDHLSL

        Pass { Name "Downsample" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return Kawase(i, 0.0); }
        ENDHLSL }
        Pass { Name "Blur1" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return Kawase(i, 0.5); }
        ENDHLSL }
        Pass { Name "Blur2" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return Kawase(i, 1.5); }
        ENDHLSL }
        Pass { Name "Blur3" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return Kawase(i, 2.5); }
        ENDHLSL }
        Pass { Name "Blur4" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(Varyings i) : SV_Target { return Kawase(i, 3.5); }
        ENDHLSL }
    }
}
