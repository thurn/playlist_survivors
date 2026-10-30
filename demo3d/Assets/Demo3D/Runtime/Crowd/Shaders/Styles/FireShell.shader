// `--monster-style fire`, the flames (crowd.md, Monster styles): stylelab's FireShell, drawn as
// the type's shell (MonsterStyle.Shells) on the same instances. A second copy of the mesh pushed
// out along its normals, its upper faces stretched up into licking tongues, drawn additively in
// flowing noise with a black-to-white-hot ramp, brighter as the beat wave passes and flaring as
// the body burns away. Being additive light it casts no shadow, writes no depth and no motion
// vectors: under TAA it reprojects with what is behind it (crowd.md).
Shader "Demo3D/Styles/Shells/FireShell"
{
    Properties
    {
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _Thickness ("Thickness (metres)", Float) = 0.07
        _Lick ("Tongue height (metres)", Float) = 0.35
        _Intensity ("Intensity", Float) = 0.7
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "FireShellInput.hlsl"
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
            #include "FireShellForward.hlsl"
            ENDHLSL
        }
    }
}
