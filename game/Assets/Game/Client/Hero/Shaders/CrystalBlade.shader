// The hero's white crystal blade (unity-game.md, The Hero; HeroBlade): demo3d's CrystalBlade.shader,
// copied and owned, on the beat globals and lighting the structures share (StructureCommon.hlsl). A
// white gem with a light inside, brightest along the blade's spine, that throbs as the beat wave
// leaves the hero's feet and flares on each blow (_BladeFlare, set by HeroBlade) past the bloom's
// threshold. It passes less of the scene behind it than a crystal monster (_Behind), so a bright
// arena doesn't wash its facets out.
//
// It draws in the crystal's queue, with a twin material in the last opaque queue that runs only its
// MotionVectors pass, so a swing reprojects.
Shader "Game/CrystalBlade"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.6, 0.65, 0.72, 1)
        _Refraction ("Refraction", Float) = 0.05
        _Facets ("Faceting", Range(0, 1)) = 0.95
        _Behind ("Light through", Float) = 0.2
        _Glow ("Spine light", Float) = 1.0
        _BladeFlare ("Blow flare (0..1)", Float) = 0
        _Gain ("Gain", Float) = 1
        _Peak ("Peak", Float) = 2.4
        _HeightM ("Height at scale 1 (m)", Float) = 1.85
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Behind; half _Glow; half _BladeFlare;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END
        #include "../../Arena/Shaders/StructureCommon.hlsl"

        struct BladeAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 core : TEXCOORD1; // x 1 on the spine, 0 on the edges; y 0 at the guard, 1 at the tip
            float4 bary : TEXCOORD3; // barycentrics and a value per triangle
        };
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                float2 core : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(BladeAttributes v)
            {
                Varyings o = (Varyings)0;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bary = v.bary;
                o.core = v.core;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // demo3d's CrystalShade for the blade: faceted white gem refracting the camera's opaque
            // texture with a little dispersion, light trapped in each facet, hard banded reflections,
            // facet edges that catch the light and glints as the beat wave passes, plus the glow.
            half4 frag(Varyings i) : SV_Target
            {
                float3 positionWS = i.positionWS;
                float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
                float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                float3 N = normalize(lerp(normalize(i.normalWS), facet, _Facets));
                float NdotV = saturate(dot(N, V));

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
                float2 off = nv.xy * _Refraction;
                float3 behind;
                behind.r = SampleSceneColor(suv + off * 1.0).r;
                behind.g = SampleSceneColor(suv + off * 1.2).g;
                behind.b = SampleSceneColor(suv + off * 1.4).b;
                behind *= _Behind;

                float3 tint = _Tint.rgb;
                float wave = SL_Wave(positionWS, 6.0);
                float faceRand = i.bary.w;
                float3 color = behind * tint * 1.2 + tint * 0.08;
                color += tint * (0.1 + 0.85 * faceRand * faceRand) * (0.55 + 1.1 * wave);
                color += tint * pow(NdotV, 3.0) * 0.35 * (1.0 + wave);

                Light L = SL_MainLight(positionWS);
                float3 R = reflect(-V, N);
                float fres = pow(1.0 - NdotV, 3.0);
                float band = smoothstep(0.42, 0.58, frac(dot(R, float3(1.7, 2.3, 1.1)) + faceRand * 0.35));
                float3 env = lerp(float3(0.02, 0.02, 0.05), float3(0.65, 0.7, 1.0), band) * lerp(float3(1, 1, 1), tint, 0.35);
                color += env * (0.3 + fres * 1.3) + tint * fres * 0.7;
                float glint = pow(saturate(dot(R, L.direction)), 300.0) * 25.0 * L.shadowAttenuation;
                color += glint * L.color;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(positionWS, i.positionCS, N, V, 0.0, 250.0, addDiffuse, addSpec);
                color += addSpec * 6.0 + addDiffuse * tint * 0.15;

                color += SL_Wire(i.bary.xyz, 1.0) * (tint * 0.25 + 0.1) * (0.4 + wave);
                float twinkle = step(0.88, SL_Hash1(faceRand * 91.7 + SL_WaveBeat(positionWS) * 13.1));
                color += twinkle * wave * (tint * 3.0 + 1.5);

                // The light inside: a thin line down the spine, fading toward the tip, up on each beat as
                // the wave leaves the hero; each blow lights the whole blade.
                float spine = pow(i.core.x, 6.0);
                float beat = SL_Wave(positionWS, 5.0);
                float glow = _Glow * spine * (1.0 - 0.4 * i.core.y) * (0.7 + 0.6 * beat)
                           + _BladeFlare * (0.25 + 0.5 * spine);
                color += tint * 3.0 * glow;
                return half4(MixFog(SL_GradeOf(color * _Gain, _Peak), i.fogFactor), 1);
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
            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(BladeAttributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Back

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
}
