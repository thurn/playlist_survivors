// The trailer's impact lens hit (LensHitFeature; trailer.md, "Look"), on the post stack's output: a radial blur that
// streaks each pixel toward the impact's screen position, and a chromatic-aberration spike that splits red outward and
// blue inward across it. Both grow smoothly with the pixel's distance from the impact, to their full length 0.6 of a
// screen's width away. At strength 0 every sample is the pixel itself.
Shader "Hidden/Demo3D/LensHit"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "LensHit"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _LensHit;      // xy: the impact (viewport), z: the longest streak, w: the aberration's split (shares of the width)
            float _LensHitAspect; // the target's width over its height

            #define TAPS 16

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                // In widths, so the streak and the split are round, whatever the aspect.
                float2 toWidth = float2(1.0, 1.0 / _LensHitAspect);
                float2 v = (uv - _LensHit.xy) / toWidth;
                float dist = length(v);
                float2 dir = dist > 1e-5 ? v / dist : float2(0.0, 0.0);
                float reach = smoothstep(0.0, 0.6, dist);
                float2 streak = dir * (_LensHit.z * reach) * toWidth; // toward the impact, in uv
                float2 split = dir * (_LensHit.w * reach) * toWidth;

                half3 sum = 0;
                half weight = 0;
                [unroll]
                for (int i = 0; i < TAPS; i++)
                {
                    float k = i / (TAPS - 1.0);
                    half w = 1.0 - 0.85 * k; // the streak fades away from the pixel
                    float2 p = uv - streak * k;
                    half r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, p + split, 0).r;
                    half g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, p, 0).g;
                    half b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, p - split, 0).b;
                    sum += w * half3(r, g, b);
                    weight += w;
                }
                return half4(sum / weight, 1.0);
            }
            ENDHLSL
        }
    }
}
