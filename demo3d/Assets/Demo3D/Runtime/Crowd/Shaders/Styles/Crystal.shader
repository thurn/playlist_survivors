// `--monster-style crystal` (crowd.md, Monster styles): stylelab's "Living crystal" on the
// bone-texture crowd's style mesh. Faceted gemstone bodies drawn after the opaque scene: each
// facet refracts what is behind it (the camera's opaque texture, which MonsterStyle turns on for
// this style only) with a little dispersion, tinted by the type's gem colour. Facet edges catch
// the light, a few facets glint as the beat wave passes, and the core glows on the beat (the surface
// is CrystalCommon.hlsl's, shared with the shards). Its own death flies the triangles apart, in every
// pass; in the demo CrystalShatter hides a dying monster at its hold and its baked shards break
// instead. In the transparent queue URP runs none of its depth or motion passes, so under TAA a
// moving crystal smears (crowd.md, Monster styles).
Shader "Demo3D/Styles/Crystal"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
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

        #define SL_STYLE_MESH
        #define SL_DISSOLVE(rest, death) 0.0
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) SL_ShatterDisplace(v, m, slot, positionWS, 1.6);
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
            #include "CrystalCommon.hlsl"

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                float shatter = SL_ShatterT(SL_DEATH(i), i.bary.w);
                return CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, SL_HIT(i), shatter, 0, i.fogFactor);
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
