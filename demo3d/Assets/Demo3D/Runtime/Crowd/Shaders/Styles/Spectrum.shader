// `--monster-style spectrum` (crowd.md, Monster styles): stylelab's "Spectrum" (Synth.shader) on
// the bone-texture crowd's style mesh. Synthesizer creatures: a black lacquer body with a
// thin-film rainbow that shifts with the viewing angle, height and distance from the hero, so
// colour bands sweep through the pack on every beat. Every triangle edge glows as a neon wire;
// equalizer bars climb the body on each beat, and the bass swells the mesh. Death bursts into
// rainbow shards. The swell and the shards move every pass.
Shader "Demo3D/Styles/Spectrum"
{
    Properties
    {
        _Hue ("Hue offset", Range(0, 1)) = 0
        _WireGlow ("Wire glow", Float) = 2.5
        _Bulge ("Bass swell (metres)", Float) = 0.035
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _Hue; half _WireGlow; float _Bulge;
            half _Gain;
            float _HeightM;
        CBUFFER_END

        // The bass swell along the normal, a ripple up the body; then the shards.
        #define SL_STYLE_MESH
        #define SL_DISSOLVE(rest, death) 0.0
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
            positionWS += normalWS * _Bulge * _SL_Pulse * (0.6 + 0.4 * sin(saturate(v.positionOS.y / max(_HeightM, 0.1)) * 12.0 - _SL_Time * 10.0)); \
            SL_ShatterDisplace(v, m, slot, positionWS, 1.3);
        #include "StyleCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                float shatter = SL_ShatterT(SL_DEATH(i), i.bary.w);
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                float d = distance(i.positionWS.xz, _SL_HeroPos.xz);
                float hue = frac(_Hue + h01 * 0.45 + (1.0 - NdotV) * 0.5 + d * 0.035 - _SL_Beat * 0.125);
                float wave = SL_Wave(i.positionWS, 6.0);

                Light L = SL_MainLight(i.positionWS);
                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), 120.0) * L.shadowAttenuation;
                float fres = pow(1.0 - NdotV, 2.0);
                float3 color = float3(0.008, 0.006, 0.02);
                color += SL_Hue(hue) * (0.1 + 1.5 * fres) * (0.8 + 0.6 * wave);
                color += spec * 2.5 * L.color;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 120.0, addDiffuse, addSpec);
                color += addSpec * 2.0;

                float wire = SL_Wire(i.bary.xyz, 1.6);
                color += SL_Hue(hue + 0.33) * wire * _WireGlow * (0.35 + 2.2 * wave);

                // Equalizer bars: bands up the body, lit up to a level set by the beat.
                float level = 0.08 + _SL_Pulse * 0.75 + _SL_Energy * 0.15;
                float bar = step(frac(h01 * 9.0), 0.3) * step(h01, level);
                color += SL_Hue(h01 * 0.6 + _Hue) * bar * 0.5;

                color += float3(1.0, 1.0, 1.0) * SL_HIT(i) + SL_Hue(hue + i.bary.w) * 2.5 * shatter;
                return half4(MixFog(color * _Gain, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define SL_PASS_SHADOWCASTER
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_DEPTHONLY
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define SL_PASS_DEPTHNORMALS
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ZWrite On
            ColorMask RG
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_MOTIONVECTORS
            #include "StylePasses.hlsl"
            ENDHLSL
        }
    }
}
