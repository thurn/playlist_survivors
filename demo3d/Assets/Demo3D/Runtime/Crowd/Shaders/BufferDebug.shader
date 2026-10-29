// TD5's buffer views (BufferDebugPass): pass 0 shows the motion vector texture (mid-grey for no
// motion), pass 1 the camera normals texture (world normal × 0.5 + 0.5).
Shader "Hidden/Demo3D/BufferDebug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        ENDHLSL

        Pass
        {
            Name "Motion"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float2 mv = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord).xy;
                return half4(saturate(0.5 + mv * 60), 0.5, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Normals"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, i.texcoord).xyz;
                return half4(saturate(n * 0.5 + 0.5), 1);
            }
            ENDHLSL
        }
    }
}
