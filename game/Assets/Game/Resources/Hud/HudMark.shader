// The HUD's marks in world space (Client/Hud/BeatGem.cs), each drawn on a quad whose uv runs -1..1:
// a ring (the beat gem's closing ring, the life ring under the hero, an arc of it filled), a
// faceted crystal diamond (the gem: four facets lit from the upper left, a white core), or a
// diamond's outline (the gem's beat burst). Unlit and HDR, so the night post's bloom catches them;
// alpha-blended; the depth test is the material's (the overhead marks draw over everything).
Shader "Game/HudMark"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 1, 1, 1)
        _Shape ("Shape (0 ring, 1 diamond, 2 diamond outline)", Float) = 0
        _Inner ("Inner edge (ring and outline, share of the radius)", Float) = 0.8
        _Fill ("Arc filled (ring, 0..1 from the top, clockwise)", Float) = 1
        _Core ("Core (diamond, 0..1)", Float) = 0.4
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "IgnoreProjector" = "True" }

        Pass
        {
            Name "HudMark"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Shape;
                half _Inner;
                half _Fill;
                half _Core;
                half _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = i.uv;
                return o;
            }

            // Antialiased step: 1 inside (d < 0), over about a pixel.
            half Inside(float d)
            {
                float w = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / w);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv;
                half3 c = _Color.rgb;
                half a;
                if (_Shape < 0.5)
                {
                    float r = length(p);
                    a = Inside(r - 1) * Inside(_Inner - r);
                    // The arc: clockwise from the top (+v), as far as _Fill goes round.
                    float turn = frac(atan2(p.x, p.y) / (2 * PI) + 1);
                    a *= _Fill >= 1 ? 1 : Inside(turn - _Fill);
                }
                else
                {
                    float d = abs(p.x) + abs(p.y);
                    if (_Shape < 1.5)
                    {
                        a = Inside(d - 1);
                        // Four facets lit from the upper left, and a white core.
                        half facet = p.x < 0 ? (p.y > 0 ? 1.0 : 0.62) : (p.y > 0 ? 0.8 : 0.45);
                        half core = Inside(d - _Core);
                        c = lerp(c * facet, max(c, half3(1, 1, 1)) * (1 + 0.5 * facet), core);
                    }
                    else a = Inside(d - 1) * Inside(_Inner - d);
                }
                return half4(c, a * _Color.a);
            }
            ENDHLSL
        }
    }
}
