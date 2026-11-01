// The crystal structures (unity-game.md, "The Look" and "Crystal Structures"): faceted gem refracting
// the camera's opaque texture, its facets world-space cells. demo3d's `--structure-style crystal`
// (its Structures/Crystal.shader and the crowd's CrystalCommon.hlsl under SL_STATIC), copied and
// owned for static meshes. The structures stay out of the bake: the crystal takes no ambient, so it
// is lit by the moon (its realtime shadow and the static shadow map), Forward+'s lights (the
// hero's above all) and what it refracts. Each site has its own gem colour (_SL_Tint,
// StructureStyle's hues across the spectrum). Alpha clip and the canopy fade follow the kit
// material StructureStyle replaces.
Shader "Game/StructureCrystal"
{
    Properties
    {
        [HDR] _Tint ("Gem (a site's _SL_Tint replaces it)", Color) = (0.31, 0.53, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.05
        _Facets ("Faceting", Range(0, 1)) = 0.85
        _Gain ("Structure gain (StructureStyle tuning)", Float) = 0.16
        _Peak ("Structure peak (StructureStyle tuning)", Float) = 0.2
        _HeightM ("Piece height (m), per renderer", Float) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (the kit material's)", Float) = 2
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
        #include "StructureCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "CrystalSurface.hlsl"

            SLVaryings vert(SLAttributes v) { return SL_Finish(v, SL_Skin(v)); }

            // A shard's fresh inside faces glow while it breaks (_SL_PieceFx.w) and stay more saturated.
            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i)
                float inside = SL_INSIDE(i);
                float3 tint = _SL_Tint.a > 0 ? _SL_Tint.rgb : _Tint.rgb;
                float3 color = CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, tint, SL_HIT(i),
                                            inside * _SL_PieceFx.w, inside, 0.5, true);
                return half4(MixFog(SL_Grade(color), i.fogFactor), 1);
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
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            ShadowVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                ShadowVaryings o;
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                o.positionWS = c.positionWS;
                o.uv = v.uv;
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target
            {
                SL_StaticClip(i.positionCS, i.positionWS, i.uv, false);
                return 0;
            }
            ENDHLSL
        }
    }
}
