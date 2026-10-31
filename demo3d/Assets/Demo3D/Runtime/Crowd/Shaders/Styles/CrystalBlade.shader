// The hero's crystal blade (crowd.md, The Hero; HeroBlade): Crystal.shader's surface
// (CrystalCommon.hlsl) on a cut-crystal sword drawn by a MeshRenderer on the hero's hand, under every
// monster style. White gem with a light inside that is brightest along the blade's spine, throbs on
// the beat (StyleBeat's wave, which leaves the hero's feet) and flares on each blow (_BladeFlare, per
// material, set by HeroBlade), past the bloom's threshold, so the blade glows. It passes less of
// the scene behind it than a crystal monster (_Behind), so a bright arena doesn't wash its facets out.
//
// Like CrystalShard, it draws in Crystal's queue with a twin material in the last opaque queue that
// runs only its MotionVectors pass, so a swing reprojects under TAA.
Shader "Demo3D/Styles/CrystalBlade"
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

        #include "StyleCommon.hlsl"

        struct BladeAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 core : TEXCOORD1; // x 1 on the spine, 0 on the edges; y 0 at the guard, 1 at the tip
            float4 bary : TEXCOORD3; // barycentrics and a value per triangle, as the style mesh's
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
            #include_with_pragmas "StylePragmas.hlsl"
            #define CRYSTAL_BEHIND _Behind
            #include "CrystalCommon.hlsl"

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

            half4 frag(Varyings i) : SV_Target
            {
                if (_Demo3DIdMask > 0.5) return half4(0, 0, 0, 1); // TD15's ID render: the hero draws black
                // The light inside: a thin line down the spine, fading toward the tip, up on each beat as the
                // wave leaves the hero; each blow lights the whole blade. The facets' own light
                // (CrystalShade) carries the rest, so the cut reads.
                float spine = pow(i.core.x, 6.0);
                float beat = SL_Wave(i.positionWS, 5.0);
                float glow = _Glow * spine * (1.0 - 0.4 * i.core.y) * (0.7 + 0.6 * beat)
                           + _BladeFlare * (0.25 + 0.5 * spine);
                return CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, 0, glow, 0, i.fogFactor);
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
