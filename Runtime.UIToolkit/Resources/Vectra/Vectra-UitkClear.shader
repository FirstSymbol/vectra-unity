// Writes transparent black over the whole viewport: clears the region of a surface page that was just given out.
Shader "Hidden/Vectra/UitkClear"
{
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return float4(vertex.xy, 0.0, 1.0);
            }

            float4 frag() : SV_Target
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
