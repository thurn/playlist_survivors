// The edge hero (neon-look.md, The Axes > Hero; HeroLook.cs, option `edge`, the edge preset's): the
// dark-core-edges monsters' grammar on the hero light rung. Smoked glass a little lit in hero.body (where
// the monsters' is black), a strong fresnel rim and lines on the knight's feature edges (HeroFacets) in
// hero.body, thicker than a monster's, and a core of light in its chest seen through the glass, so the
// small figure at the trailer's framing is a bright cut-crystal knight among the swarm's coloured
// outlines. Gains are tuned so the knight sits on the hero light rung (L* p90 about 98, under a fifth of
// it clipped) at the game's 36 m framing (game/evidence/look/hero); the lines are 1.8 px, not 1.4, for the game's own
// wider framing at the run's peak, where the 1.4 px knight mid-swing sat only 3 L* over the swarm (ps-wnj.18). The blade is the same glass with every facet edged, its spine lit and its blow flare kept
// (HeroLook.hlsl). The beat lifts the lines and the core. Opaque, in the crystal's queue as the monsters.
Shader "Look/HeroEdge"
{
    Properties
    {
        [HDR] _Body ("hero.body (linear)", Color) = (1, 0.97, 0.92, 1)
        [HDR] _Accent ("hero.trail (linear)", Color) = (1, 0.9, 0.5, 1)
        [HDR] _EmissionColor ("Added light (the low-health pulse)", Color) = (0, 0, 0, 1)
        _Blade ("The blade's material", Float) = 0
        _AllEdges ("Every triangle edge a line (the blade)", Float) = 0
        _EdgePx ("Line width (px)", Float) = 1.8
        _Glow ("Blade spine light", Float) = 0.6
        _BladeFlare ("Blow flare (0..1, set by HeroBlade)", Float) = 0
        _Glass ("Glass light", Float) = 0.03
        _Rim ("Rim gain", Float) = 0.6
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 1.0
        _Beat ("Line and core lift on the beat", Float) = 0.15
        _CoreGlow ("Chest core gain", Float) = 0.35
        _CoreSize ("Chest core radius (m)", Float) = 0.22
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
            half _Glass; half _Rim; half _RimPower; half _Edge; half _Beat; half _CoreGlow; half _CoreSize;
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
                HeroNormal(i.positionWS, i.normalWS, 0.6, nv);
                float beat = HeroBeat(i.positionWS);
                float3 body = _Body.rgb;
                // The glass: each facet a shade apart (the triangle's random), lit a little, never black.
                float3 color = body * _Glass * (0.6 + 0.8 * i.bary.w);
                color += body * _Rim * pow(1 - nv, _RimPower);
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                color += body * _Edge * lit * (0.75 + 0.25 * (1 - nv)) * (1 + _Beat * beat);
                // The chest's core: where the view ray passes near a point 1.15 m over the feet (the knight's only).
                float3 chest = _SL_HeroPos.xyz + float3(0, 1.15, 0);
                float3 ray = normalize(i.positionWS - _WorldSpaceCameraPos);
                float d = length(cross(chest - _WorldSpaceCameraPos, ray));
                float r = max(_CoreSize, 1e-3);
                color += body * exp(-(d * d) / (r * r)) * _CoreGlow * (1 + _Beat * beat) * (1 - _Blade);
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
