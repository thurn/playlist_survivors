// The prism hero (neon-look.md, The Axes > Hero; HeroLook.cs, option `prism`, the prism preset's): the
// refractive gem monsters' grammar on the hero light rung. A white gem full of trapped light: the void
// and grid behind it (the camera's opaque texture) bent through each facet and split a little toward
// hero.trail, under light caught inside every facet at its own brightness, a band of reflected white
// per facet that jumps as the view turns, a rim and fine white feature lines. Where a gem monster is
// mostly dark glass round a narrow band, the hero is mostly light, so it outshines the swarm. The beat
// sparkles a few facets. The blade is the same gem with its spine lit and its blow flare kept
// (HeroLook.hlsl). Opaque, in the crystal's queue as the monsters; the camera's opaque texture must be
// on (HeroLooks.Refracts).
Shader "Look/HeroGem"
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
        _Refraction ("Refraction (screen share)", Float) = 0.04
        _Behind ("Light through", Float) = 0.4
        _Inner ("Trapped light", Float) = 0.11
        _Band ("Reflected band", Float) = 0.7
        _Rim ("Rim gain", Float) = 0.42
        _Edge ("Line gain", Float) = 0.55
        _Sparkle ("Facets that sparkle on the beat", Float) = 1.2
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
            half _Refraction; half _Behind; half _Inner; half _Band; half _Rim; half _Edge; half _Sparkle;
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            half4 frag(HeroVaryings i) : SV_Target
            {
                float nv;
                float3 N = HeroNormal(i.positionWS, i.normalWS, 0.9, nv);
                float beat = HeroBeat(i.positionWS);
                float3 body = _Body.rgb;
                float r = i.bary.w;
                // What is behind, bent through the facet, split toward hero.trail on one side.
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 off = mul((float3x3)UNITY_MATRIX_V, N).xy * _Refraction;
                float3 behind = float3(SampleSceneColor(suv + off).r, SampleSceneColor(suv + off * 1.15).g, SampleSceneColor(suv + off * 1.3).b);
                float3 color = behind * lerp(body, _Accent.rgb, 0.4) * _Behind;
                // Light trapped in each facet, at its own brightness.
                color += body * _Inner * (0.55 + 0.45 * r * r);
                // A band of reflected light per facet, jumping between facets as the view turns, brightest at grazing.
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 R = reflect(-V, N);
                float band = smoothstep(0.45, 0.55, frac(dot(R, float3(1.7, 2.3, 1.1)) + r * 0.35));
                float fres = pow(1 - nv, 3);
                color += lerp(body, _Accent.rgb, 0.3) * band * _Band * (0.35 + 0.65 * fres);
                color += body * _Rim * fres;
                color += body * _Edge * EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                float twinkle = step(0.9, SL_Hash1(r * 91.7 + SL_WaveBeat(i.positionWS) * 13.1));
                color += body * twinkle * beat * _Sparkle;
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
