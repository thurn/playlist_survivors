// `--structure-style painted` (environment.md, Structure styles): the horde's lacquered colour
// strip on the kit's light and dark, with a structure palette (StructureStyle). The crowd's
// Painted.shader code on a static mesh: its PaintedInput.hlsl and PaintedForward.hlsl under
// SL_STATIC (StyleCommon.hlsl), so the structures and the horde share one look. Lit as the kit is
// (light probes, the moon with the static shadow map, Forward+'s lights); alpha clip and the canopy
// fade follow the kit material StructureStyle replaces. Per renderer: _SL_Piece, _SL_PieceFx and
// _HeightM.
Shader "Demo3D/Structures/Painted"
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
        _Gain ("Structure gain (StructureStyle tuning)", Float) = 1
        _Peak ("Structure peak (StructureStyle tuning)", Float) = 1000
        _HeightM ("Piece height (m), per renderer", Float) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (the kit material's)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define SL_STATIC
        #include "../../../Crowd/Shaders/Styles/PaintedInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "../../../Crowd/Shaders/Styles/StylePragmas.hlsl"
            #include "../../../Crowd/Shaders/Styles/PaintedForward.hlsl"
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
            #define SL_PASS_SHADOWCASTER
            #include "../../../Crowd/Shaders/Styles/StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_DEPTHONLY
            #include "../../../Crowd/Shaders/Styles/StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define SL_PASS_DEPTHNORMALS
            #include "../../../Crowd/Shaders/Styles/StylePasses.hlsl"
            ENDHLSL
        }
    }
}
