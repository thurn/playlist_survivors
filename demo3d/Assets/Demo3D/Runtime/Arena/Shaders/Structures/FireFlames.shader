// `--structure-style fire` (environment.md, Structure styles): the flames: FireShell on a second
// renderer beside each burning structure (the FireShell approach), additive, casting no shadow. The
// crowd's FireShell.shader code on a static mesh: its FireShellInput.hlsl and FireShellForward.hlsl
// under SL_STATIC (StyleCommon.hlsl), so the structures and the horde share one look. Lit as the
// kit is (light probes, the moon with the static shadow map, Forward+'s lights); alpha clip and the
// canopy fade follow the kit material StructureStyle replaces. Per renderer: _SL_Piece, _SL_PieceFx
// and _HeightM.
Shader "Demo3D/Structures/FireFlames"
{
    Properties
    {
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _Thickness ("Thickness (metres)", Float) = 0.07
        _Lick ("Tongue height (metres)", Float) = 0.35
        _Intensity ("Intensity", Float) = 0.7
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
        #include "../../../Crowd/Shaders/Styles/FireShellInput.hlsl"
        ENDHLSL

        Pass
        {
            Name "Flames"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "../../../Crowd/Shaders/Styles/FireShellForward.hlsl"
            ENDHLSL
        }
    }
}
