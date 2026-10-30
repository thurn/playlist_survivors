// `--monster-style painted` (crowd.md, Monster styles): stylelab's "Polished painted", the Deep
// Rock Galactic: Survivor look, on the bone-texture crowd. The albedo's colours are thrown away
// and only its light and dark kept, looked up in a colour strip per type. Shadows turn a deeper,
// saturated hue instead of grey; a hard highlight and a lacquered sheen (an analytic matcap)
// make the shell glossy; a complementary rim lifts the silhouette off the ground; glowing spots
// pulse as the beat wave passes. Its other passes are the styles' shared ones (StylePasses.hlsl).
Shader "Demo3D/Styles/Painted"
{
    Properties
    {
        _BaseMap ("Albedo (only its value is used)", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Ramp ("Type colour strip", 2D) = "white" {}
        _LumRange ("Albedo value range (min, max)", Vector) = (0.05, 0.6, 0, 0)
        _ShadowColor ("Shadow tint", Color) = (0.35, 0.2, 0.6, 1)
        [HDR] _RimColor ("Rim", Color) = (1.5, 0.6, 0.2, 1)
        [HDR] _SpecColor ("Highlight", Color) = (1.4, 1.35, 1.25, 1)
        _Gloss ("Highlight tightness", Float) = 48
        _Sheen ("Sheen", Range(0, 2)) = 0.8
        [HDR] _SpotColor ("Glow spots", Color) = (0.2, 2.5, 2.2, 1)
        _SpotScale ("Glow spot scale (per metre)", Float) = 5
        _SpotAmount ("Glow spot threshold", Range(0, 1)) = 0.68
        _Ambient ("Ambient", Float) = 0.9
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "PaintedInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"
            #include "PaintedForward.hlsl"
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
