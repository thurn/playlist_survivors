// `--structure-style spectrum` (environment.md, Structure styles): black lacquer with a thin-film
// rainbow, a neon lattice (SL_STATIC_GRID) and equalizer bars climbing each piece on the beat. The
// crowd's Spectrum.shader code on a static mesh: its SpectrumInput.hlsl and SpectrumForward.hlsl
// under SL_STATIC (StyleCommon.hlsl), so the structures and the horde share one look. Lit as the
// kit is (light probes, the moon with the static shadow map, Forward+'s lights); alpha clip and the
// canopy fade follow the kit material StructureStyle replaces. Per renderer: _SL_Piece, _SL_PieceFx
// and _HeightM.
Shader "Demo3D/Structures/Spectrum"
{
    Properties
    {
        _Hue ("Hue offset", Range(0, 1)) = 0
        _WireGlow ("Wire glow", Float) = 2.5
        _Bulge ("Bass swell (metres)", Float) = 0.035
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
        #define SL_STATIC_GRID
        #include "../../../Crowd/Shaders/Styles/SpectrumInput.hlsl"
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
            #include_with_pragmas "../../../Crowd/Shaders/Styles/StylePragmas.hlsl"
            #include "../../../Crowd/Shaders/Styles/SpectrumForward.hlsl"
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
