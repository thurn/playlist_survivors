// The trailer's grade (Grade.cs; the frozen look package's LookPost.shader, its scene stage, ported by review): before
// URP's post, on the scene's light, the wide soft halo's pyramid (prefilter, down, up) and the scene pass, which exposes,
// sinks the void under a knee, adds the halo and tonemaps with the filmic curve that keeps a saturated emitter's hue. Each
// pass reads _BlitTexture; _GradeTexel is its texel size and _GradeExtra the up pass's level and the scene pass's halo.
Shader "Hidden/Champions/Grade"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _GradeTexel; // xy: the source's texel size, zw: its size
        float4 _GradeHalo;  // x: threshold, y: intensity per level, z: the glow's saturation, w: the exposure (linear)
        float4 _GradeTone;  // x: unused, y: the shoulder, z: how far a core past white whitens, w: the input that whitens fully
        float4 _GradeVoid;  // x: the toe's knee (0: none), y: its power
        TEXTURE2D_X(_GradeExtra);

        half3 Sample(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb; }

        half Luma(half3 c) { return dot(c, half3(0.2126, 0.7152, 0.0722)); }
        ENDHLSL

        // 0: the halo's top level: a 4x4 box of the frame exposed, what is over the threshold (a soft knee), its glow saturated.
        Pass
        {
            Name "HaloPrefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord, t = _GradeTexel.xy;
                half3 c = 0.25 * _GradeHalo.w * (Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                                               + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1)));
                half l = Luma(c);
                half knee = 0.5 * _GradeHalo.x;
                half soft = clamp(l - _GradeHalo.x + knee, 0, 2 * knee);
                soft = soft * soft / (4 * knee + 1e-4);
                half w = max(soft, l - _GradeHalo.x) / max(l, 1e-4);
                half3 glow = c * w;
                glow = max(0, lerp(Luma(glow).xxx, glow, _GradeHalo.z));
                return half4(min(glow, 64), 1);
            }
            ENDHLSL
        }

        // 1: down a level: the dual filter's five taps.
        Pass
        {
            Name "HaloDown"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord, t = _GradeTexel.xy;
                half3 c = Sample(uv) * 4 + Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                        + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1));
                return half4(c / 8, 1);
            }
            ENDHLSL
        }

        // 2: up a level: the level below (the source) by the dual filter's eight taps, plus this level (_GradeExtra).
        Pass
        {
            Name "HaloUp"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord, t = _GradeTexel.xy;
                half3 c = Sample(uv + t * float2(-2, 0)) + Sample(uv + t * float2(2, 0))
                        + Sample(uv + t * float2(0, -2)) + Sample(uv + t * float2(0, 2))
                        + 2 * (Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                             + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1)));
                half3 own = SAMPLE_TEXTURE2D_X_LOD(_GradeExtra, sampler_LinearClamp, uv, 0).rgb;
                return half4(c / 12 + own, 1);
            }
            ENDHLSL
        }

        // 3: the scene: exposed, the void's toe, plus the halo, then the filmic tonemap.
        Pass
        {
            Name "Scene"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // The brightest channel rolls off past the shoulder toward 1 and the others scale with it, so hue and
            // saturation hold; a core run past white leans toward white with how far past it ran.
            float3 Filmic(float3 c)
            {
                float m = max(c.r, max(c.g, c.b));
                if (m <= 1e-5) return c;
                float a = _GradeTone.y;
                float mm = m < a ? m : a + (1 - a) * (1 - exp(-(m - a) / (1 - a)));
                float3 kept = c * (mm / m);
                float w = _GradeTone.z * smoothstep(1, _GradeTone.w, m);
                return lerp(kept, mm.xxx, w);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 c = Sample(uv) * _GradeHalo.w;
                UNITY_BRANCH if (_GradeVoid.x > 0)
                {
                    // Under the knee the brightest channel goes as knee * (m / knee)^power, the others scaled with it.
                    float m = max(c.r, max(c.g, c.b));
                    if (m > 1e-6 && m < _GradeVoid.x) c *= pow(m / _GradeVoid.x, _GradeVoid.y - 1);
                }
                UNITY_BRANCH if (_GradeHalo.y > 0)
                    c += _GradeHalo.y * SAMPLE_TEXTURE2D_X_LOD(_GradeExtra, sampler_LinearClamp, uv, 0).rgb;
                return half4(Filmic(c), 1);
            }
            ENDHLSL
        }
    }
}
