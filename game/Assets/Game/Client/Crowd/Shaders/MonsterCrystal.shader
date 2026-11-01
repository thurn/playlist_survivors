// The crystal monsters (unity-game.md, "The Crowd" and "The Look"): demo3d's `--monster-style
// crystal` on the bone-texture crowd's style mesh, copied and owned. Faceted gemstone bodies drawn
// after the opaque scene: each facet refracts what is behind it (the camera's opaque texture) with
// a little dispersion, tinted by the role's gem colour (the instance's tint), with facet edges that
// catch the light and glints as the beat wave passes (CrystalSurface.hlsl, shared with the
// structures). Deaths shatter (the GPU shards), so the body has no death of its own. In the
// transparent queue URP runs no depth or motion pass for it: the game keeps MSAA, not TAA.
// Under the global _GameIdMask (the stills' ID render) it draws flat white.
Shader "Game/MonsterCrystal"
{
    Properties
    {
        [HDR] _Tint ("Gem (an instance's tint replaces it)", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Horde gain", Float) = 1
        _Peak ("Horde peak", Float) = 0.3
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END
        #include "MonsterCommon.hlsl"
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
            #include "../../Arena/Shaders/CrystalSurface.hlsl"

            MonsterVaryings vert(MonsterAttributes v) { return MonsterFinish(MonsterSkin(v), v); }

            half4 frag(MonsterVaryings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float3 color = CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, i.tint, i.fx.x, 0, 0, 1, false);
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

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings vert(MonsterAttributes v)
            {
                ShadowVaryings o;
                // URP repeats the shadow draw for each cascade: skip a monster outside this cascade's
                // light-space box (the depth axis is not tested).
                uint slot = CrowdSlot(v.instanceID);
                float height;
                float4 sphere = CrowdSphere(_Instances[slot], height);
                float4 cs = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
                float2 extent = sphere.w * float2(length(UNITY_MATRIX_VP[0].xyz), length(UNITY_MATRIX_VP[1].xyz));
                if (any(abs(cs.xy) - extent > cs.w))
                {
                    o.positionCS = float4(2, 2, 2, 1);
                    return o;
                }
                MonsterVertex c = MonsterSkin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
