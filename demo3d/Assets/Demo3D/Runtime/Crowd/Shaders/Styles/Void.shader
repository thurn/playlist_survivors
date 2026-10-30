// `--monster-style void` (crowd.md, Monster styles): stylelab's "Void", creatures cut out of the
// night sky. The body shows a starfield and a faint nebula fixed to the screen, so each monster
// looks like a hole into space, ringed by an intense coloured edge that flares as the beat wave
// passes. Death implodes it to a point at its middle (its SL_DISPLACE, so shadows, depth and
// motion vectors follow) instead of dissolving.
Shader "Demo3D/Styles/Void"
{
    Properties
    {
        _BaseMap ("Albedo (unused)", 2D) = "white" {}
        _BaseColor ("Color (unused)", Color) = (1, 1, 1, 1)
        [HDR] _RimColor ("Edge", Color) = (0.6, 0.3, 3.0, 1)
        _RimPower ("Edge tightness", Float) = 4
        _NebulaA ("Nebula A", Color) = (0.25, 0.05, 0.4, 1)
        _NebulaB ("Nebula B", Color) = (0.0, 0.3, 0.45, 1)
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
        #include "VoidInput.hlsl"
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
            #pragma multi_compile_fog
            #include "VoidForward.hlsl"
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
