// `--structure-style hologram` (environment.md, Structure styles): projections with scanlines, a
// rolling band, a projected grid (SL_STATIC_GRID) and glitch slices on the off-beats, their light
// over dim glass (premultiplied: SL_STATIC_HOLOGRAM_DIM of what is behind is taken away, so a
// see-through structure doesn't uncover the lighter ground behind the horde); no shadow caster, as a
// projection of light casts none. The crowd's Hologram.shader code on a
// static mesh: its HologramInput.hlsl and HologramForward.hlsl under SL_STATIC (StyleCommon.hlsl),
// so the structures and the horde share one look. Lit as the kit is (light probes, the moon with
// the static shadow map, Forward+'s lights); alpha clip and the canopy fade follow the kit material
// StructureStyle replaces. Per renderer: _SL_Piece, _SL_PieceFx and _HeightM.
Shader "Demo3D/Structures/Hologram"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.2, 1.4, 2.0, 1)
        _Glitch ("Glitch", Float) = 0.12
        _Gain ("Structure gain (StructureStyle tuning)", Float) = 1
        _Peak ("Structure peak (StructureStyle tuning)", Float) = 1000
        _HeightM ("Piece height (m), per renderer", Float) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (the kit material's)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define SL_STATIC
        #define SL_STATIC_GRID
        #define SL_STATIC_HOLOGRAM_DIM 0.75
        #include "../../../Crowd/Shaders/Styles/HologramInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "../../../Crowd/Shaders/Styles/HologramForward.hlsl"
            ENDHLSL
        }
    }
}
