// `--structure-style crystal` (environment.md, Structure styles): faceted gem refracting the
// camera's opaque texture (StructureStyle.NeedsOpaqueTexture), its facets world-space cells
// (SL_StaticFacets). The crowd's Crystal.shader code on a static mesh: its CrystalInput.hlsl and
// CrystalForward.hlsl under SL_STATIC (StyleCommon.hlsl), so the structures and the horde share one
// look. Lit as the kit is (light probes, the moon with the static shadow map, Forward+'s lights);
// alpha clip and the canopy fade follow the kit material StructureStyle replaces. Per renderer:
// _SL_Piece, _SL_PieceFx and _HeightM.
Shader "Demo3D/Structures/Crystal"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Structure gain (StructureStyle tuning)", Float) = 1
        _Peak ("Structure peak (StructureStyle tuning)", Float) = 1000
        _HeightM ("Piece height (m), per renderer", Float) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (the kit material's)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define SL_STATIC
        #include "../../../Crowd/Shaders/Styles/CrystalInput.hlsl"
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
            #include "../../../Crowd/Shaders/Styles/CrystalForward.hlsl"
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
    }
}
