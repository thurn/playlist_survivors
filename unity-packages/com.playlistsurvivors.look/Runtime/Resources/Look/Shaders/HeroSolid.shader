// The solid hero (neon-look.md, The Axes > Hero; HeroLook.cs, option `solid`, the solid preset's): the
// bright solid emissive monsters' grammar on the hero light rung. The knight is self-lit hero.body,
// shaded for form by a soft light from above leaning toward the camera, each facet a step apart, so it
// reads as a solid white figure brighter than any monster (whose bodies the arcade palette holds at L*
// 78 or under). No outline (the operator's review): it wins on value alone. A hot spot where it faces
// the camera lifts on the beat. The blade is the same white solid with its spine lit and its blow
// flare kept (HeroLook.hlsl). Opaque, in the crystal's queue as the monsters.
Shader "Look/HeroSolid"
{
    Properties
    {
        [HDR] _Body ("hero.body (linear)", Color) = (1, 0.97, 0.92, 1)
        [HDR] _Accent ("hero.trail (linear)", Color) = (1, 0.9, 0.5, 1)
        [HDR] _EmissionColor ("Added light (the low-health pulse)", Color) = (0, 0, 0, 1)
        _Blade ("The blade's material", Float) = 0
        _AllEdges ("Every triangle edge a line (the blade)", Float) = 0
        _EdgePx ("Line width (px)", Float) = 1.4
        _Glow ("Blade spine light", Float) = 0.6
        _BladeFlare ("Blow flare (0..1, set by HeroBlade)", Float) = 0
        _Level ("Body light", Float) = 0.34
        _Shade ("Form: the share the soft light shades", Float) = 0.55
        _Facet ("Each facet's own step", Float) = 0.08
        _Hot ("Hot spot facing the camera", Float) = 0.3
        _Beat ("Hot spot lift on the beat", Float) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Body; half4 _Accent; half4 _EmissionColor;
            half _Blade; half _AllEdges; half _EdgePx; half _Glow; half _BladeFlare;
            half _Level; half _Shade; half _Facet; half _Hot; half _Beat;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex HeroVert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.playlistsurvivors.look/Runtime/Hero/Shaders/HeroLook.hlsl"

            half4 frag(HeroVaryings i) : SV_Target
            {
                float nv;
                float3 N = HeroNormal(i.positionWS, i.normalWS, 0.7, nv);
                float beat = HeroBeat(i.positionWS);
                // The soft light: from above, leaning toward the camera, wrapped, so no side goes dark.
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 L = normalize(float3(0, 1, 0) + 0.6 * V);
                float light = saturate(dot(N, L) * 0.5 + 0.5);
                float shade = (1 - _Shade) + _Shade * light;
                float facet = 1 + _Facet * (i.bary.w * 2 - 1);
                float3 color = _Body.rgb * _Level * shade * facet;
                color += _Body.rgb * _Hot * pow(nv, 4) * (1 + _Beat * beat);
                return HeroFinish(i, color, beat, 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 vert(ShadowAttributes v) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Back
            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
}
