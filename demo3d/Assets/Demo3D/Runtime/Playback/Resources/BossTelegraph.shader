// A boss attack's telegraph on the ground (trailer.md, "The boss fight"): the zone the dragon's blow will land on, glowing
// from its windup to its impact. A bright rim, a dim fill that charges outward from the origin and reaches the rim on the
// impact, hazard bands streaming outward, a throb on each beat, and the blow's white-hot flash. Drawn additively a few
// centimetres above the ground so the bloom takes it (BossTelegraphs; uv.x: 0 at the origin to 1 at the far edge, uv.y:
// -1 to 1 across a cone, 0 on a disc). Nothing vendor-derived.
Shader "Demo3D/BossTelegraph"
{
    Properties
    {
        [HDR] _Color("Tint", Color) = (4, 0.8, 0.15, 1)
        _Progress("Windup progress", Range(0, 1)) = 0
        _Pulse("Beat throb", Range(0, 1)) = 0
        _Flash("Impact flash", Range(0, 1)) = 0
        _Fade("Fade", Range(0, 1)) = 1
        _Cone("Cone", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Telegraph"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Progress, _Pulse, _Flash, _Fade, _Cone;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fogFactor : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = i.uv.x;
                float s = abs(i.uv.y) * _Cone;
                // The rim: the far edge, and a cone's sides.
                float rim = max(smoothstep(0.95, 0.995, r) * (1 - smoothstep(0.995, 1.0, r) * 0.5), smoothstep(0.92, 0.99, s));
                // The fill charges outward, its front a bright line, reaching the rim on the impact.
                float charged = 1 - smoothstep(_Progress - 0.02, _Progress, r);
                float front = saturate(1 - abs(r - _Progress) / 0.035) * (1 - step(0.995, _Progress));
                // Hazard bands streaming outward, faster as the blow nears.
                float bands = 0.5 + 0.5 * sin((r * 7 - _Progress * (3 + 5 * _Progress)) * 6.2831853);
                float fill = 0.07 + 0.30 * charged * (0.55 + 0.45 * bands);
                float m = fill + 1.4 * rim + 1.6 * front;
                m *= 1 + 0.7 * _Pulse;
                // The blow: the whole zone white-hot, fading.
                m = lerp(m, 2.6, _Flash);
                float3 c = lerp(_Color.rgb, max(_Color.rgb, (_Color.r + _Color.g + _Color.b).xxx * 0.45), _Flash) * m * _Fade;
                c = MixFogColor(c, 0, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
