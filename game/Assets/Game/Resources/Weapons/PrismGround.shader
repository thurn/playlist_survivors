// The weapons' prism light on the ground (Client/Weapons/PrismGround.cs): a flat annulus whose
// outer edge is exactly the draw's _Radius, _Width deep (a disc when _Width is the radius), on
// CrystalWaves' ring mesh: each vertex's (cos, sin) in its xz and its edge (0 inner, 1 outer) in y.
// The ability's colour (_Tint) with a prism sheen turning around the ring, white-hot where it is
// brightest: a thin front at the edge (_Edge) and a glow through the ring (_Fill), weighted toward
// the edge or, with _Core, toward the centre. Additive and HDR, so bloom makes the glow.
Shader "Game/PrismGround"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Radius ("Radius", Float) = 1
        _Width ("Width", Float) = 1
        _Intensity ("Intensity", Float) = 1
        _Age ("Age", Float) = 0
        _Edge ("Edge", Float) = 1
        _Fill ("Fill", Float) = 1
        _Core ("Core", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "PrismGround"
            Tags { "LightMode" = "UniversalForward" }
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One One

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _Radius, _Width, _Intensity, _Age, _Edge, _Fill, _Core;
            CBUFFER_END

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float behind : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float edge = v.positionOS.y;
                float r = _Radius - _Width * (1 - edge);
                float3 local = float3(v.positionOS.x * r, 0, v.positionOS.z * r);
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(local));
                o.uv = v.uv;
                o.behind = _Width * (1 - edge); // metres in from the edge
                return o;
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float d = i.behind;
                float u = saturate(d / max(1e-3, _Width)); // 0 at the edge, 1 at the inner rim
                float front = exp(-abs(d - 0.04) / 0.05);
                float fill = lerp((1 - u) * (1 - u), u * u, _Core);
                float3 sheen = Hue(i.uv.x * 3 + _Age * 0.35 + d * 0.6);
                float3 tint = _Tint.rgb;
                float3 prism = lerp(tint, sheen * max(tint.r, max(tint.g, tint.b)), 0.3);
                // Deep in the colour (its square) where faint, white-hot as it gets bright: the edge's front and a core's centre.
                float hot = saturate(front * _Edge + fill * _Core * _Fill * 0.5);
                float3 rgb = lerp(tint * tint, float3(1, 1, 1), hot * hot) * front * _Edge * 2 + prism * tint * fill * _Fill * 0.6;
                return float4(rgb * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
