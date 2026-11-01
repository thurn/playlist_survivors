// The look's own passes round URP's post (LookPostPass; neon-look.md, Post-processing stack). Before it, on the scene's
// light: the wide soft halo's pyramid (prefilter, down, up) and the scene pass, which exposes, sinks the void, adds the halo and tonemaps
// with the filmic curve. After it: the retro pass's scanlines and CRT mask. Each pass reads _BlitTexture; _LookTexel is
// its texel size and _LookExtra the up pass's level and the scene pass's halo.
Shader "Hidden/Look/LookPost"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _LookTexel;  // xy: the source's texel size, zw: its size
        float4 _LookHalo;   // x: threshold, y: intensity per level, z: the glow's saturation, w: the exposure (linear)
        float4 _LookTone;   // x: filmic on, y: the shoulder, z: how far a core past white whitens, w: the input that whitens fully
        float4 _LookRetro;  // x: the scanlines' darkening, y: scanlines to the height, z: the CRT mask's strength
        float4 _LookVoid;   // x: the toe's knee (0: none), y: its power
        TEXTURE2D_X(_LookExtra);

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
                float2 uv = input.texcoord, t = _LookTexel.xy;
                half3 c = 0.25 * _LookHalo.w * (Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                                              + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1)));
                half l = Luma(c);
                half knee = 0.5 * _LookHalo.x;
                half soft = clamp(l - _LookHalo.x + knee, 0, 2 * knee);
                soft = soft * soft / (4 * knee + 1e-4);
                half w = max(soft, l - _LookHalo.x) / max(l, 1e-4);
                half3 glow = c * w;
                glow = max(0, lerp(Luma(glow).xxx, glow, _LookHalo.z));
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
                float2 uv = input.texcoord, t = _LookTexel.xy;
                half3 c = Sample(uv) * 4 + Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                        + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1));
                return half4(c / 8, 1);
            }
            ENDHLSL
        }

        // 2: up a level: the level below (the source) by the dual filter's eight taps, plus this level (_LookExtra).
        Pass
        {
            Name "HaloUp"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord, t = _LookTexel.xy;
                half3 c = Sample(uv + t * float2(-2, 0)) + Sample(uv + t * float2(2, 0))
                        + Sample(uv + t * float2(0, -2)) + Sample(uv + t * float2(0, 2))
                        + 2 * (Sample(uv + t * float2(-1, -1)) + Sample(uv + t * float2(1, -1))
                             + Sample(uv + t * float2(-1, 1)) + Sample(uv + t * float2(1, 1)));
                half3 own = SAMPLE_TEXTURE2D_X_LOD(_LookExtra, sampler_LinearClamp, uv, 0).rgb;
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
                float a = _LookTone.y;
                float mm = m < a ? m : a + (1 - a) * (1 - exp(-(m - a) / (1 - a)));
                float3 kept = c * (mm / m);
                float w = _LookTone.z * smoothstep(1, _LookTone.w, m);
                return lerp(kept, mm.xxx, w);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 c = Sample(uv) * _LookHalo.w;
                UNITY_BRANCH if (_LookVoid.x > 0)
                {
                    // Under the knee the brightest channel goes as knee * (m / knee)^power, the others scaled with it.
                    float m = max(c.r, max(c.g, c.b));
                    if (m > 1e-6 && m < _LookVoid.x) c *= pow(m / _LookVoid.x, _LookVoid.y - 1);
                }
                UNITY_BRANCH if (_LookHalo.y > 0)
                    c += _LookHalo.y * SAMPLE_TEXTURE2D_X_LOD(_LookExtra, sampler_LinearClamp, uv, 0).rgb;
                UNITY_BRANCH if (_LookTone.x > 0) c = Filmic(c);
                return half4(c, 1);
            }
            ENDHLSL
        }

        // 4: the retro: scanlines and the CRT mask on the graded frame.
        Pass
        {
            Name "Retro"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 c = Sample(uv);
                UNITY_BRANCH if (_LookRetro.x > 0)
                {
                    float s = 0.5 + 0.5 * cos(6.2831853 * uv.y * _LookRetro.y);
                    c *= 1 - _LookRetro.x * s * s;
                }
                UNITY_BRANCH if (_LookRetro.z > 0)
                {
                    // Vertical RGB stripes, a triad as wide as a scanline is high: each channel full on its own third,
                    // dimmed by the mask's strength off it.
                    float triad = uv.x * _LookTexel.z * _LookRetro.y / _LookTexel.w;
                    float3 stripe = 0.5 + 0.5 * cos(6.2831853 * (triad - float3(0, 1.0 / 3, 2.0 / 3)));
                    c *= 1 - _LookRetro.z * (1 - stripe);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
