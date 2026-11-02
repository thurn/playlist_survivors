// The hologram hero (neon-look.md, The Axes > Hero; HeroLook.cs, option `hologram`, the hologram
// preset's): the wireframe monsters' grammar on the hero light rung. A figure of light added over the
// world: hero.body lines on the knight's feature edges (HeroFacets), thicker and brighter than a
// monster's, a rim and a fill far denser than a monster's faint one, in hero.body leaning to hero.trail
// toward the silhouette, so the hero is the one solid-looking body of light in a swarm of wire. The
// beat sends a band up it from its feet. The first pass writes depth only, so the second adds the
// front surfaces alone (blended premultiplied: the light added, what is behind dimmed by the veil). The
// blade is wire too, every facet edged, its spine lit and its blow flare kept (HeroLook.hlsl). No
// shadow: it is light.
Shader "Look/HeroHologram"
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
        _Fill ("Fill face-on", Float) = 0.05
        _FillRim ("Fill toward the silhouette", Float) = 0.25
        _Veil ("Veil: how much it dims what is behind", Float) = 0.3
        _Rim ("Rim gain", Float) = 1.0
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 1.8
        _Beat ("Line lift on the beat", Float) = 0.15
        _Sweep ("Beat band gain", Float) = 0.5
        _SweepWidth ("Beat band half-width (share of height)", Float) = 0.08
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
            half _Fill; half _FillRim; half _Veil; half _Rim; half _RimPower; half _Edge; half _Beat; half _Sweep; half _SweepWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "HeroDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex HeroVert
            #pragma fragment frag
            #include "Packages/com.playlistsurvivors.look/Runtime/Hero/Shaders/HeroLook.hlsl"
            half4 frag(HeroVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex HeroVert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.playlistsurvivors.look/Runtime/Hero/Shaders/HeroLook.hlsl"

            half4 frag(HeroVaryings i) : SV_Target
            {
                float nv;
                HeroNormal(i.positionWS, i.normalWS, 0.5, nv);
                float beat = HeroBeat(i.positionWS);
                float edge = 1 - nv;
                float3 body = _Body.rgb;
                float3 fillColor = lerp(body, _Accent.rgb, 0.35 * edge);
                float3 color = fillColor * (_Fill * (0.85 + 0.3 * i.bary.w) + _FillRim * edge * edge);
                color += body * _Rim * pow(edge, _RimPower);
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                color += body * _Edge * lit * (0.8 + 0.2 * edge) * (1 + _Beat * beat);
                // The beat's band: at the feet on the beat's frame, rising to the head as the pulse decays.
                float x = (HeroHeight01(i.positionWS) - (1 - _SL_Pulse)) / max(_SweepWidth, 1e-3);
                color += body * _Sweep * _SL_Pulse * exp(-x * x) * (1 - _Blade);
                return HeroFinish(i, color, beat, _Veil);
            }
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
