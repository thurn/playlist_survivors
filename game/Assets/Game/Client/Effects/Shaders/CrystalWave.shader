// The sword's crystal wave (CrystalWaves.cs): a flat annulus on the ground whose outer edge is
// exactly the draw's _Radius, _Width deep. The mesh carries each vertex's (cos, sin) in its xz and
// its edge (0 inner, 1 outer) in y. A thin white-hot front at the edge, then a trail of crystal
// light with a rainbow sheen that turns around the ring, additive and HDR so bloom makes the glow.
Shader "Game/CrystalWave"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "CrystalWave"
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
            float _Radius, _Width, _Intensity, _Age;
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
                o.behind = _Width * (1 - edge); // metres behind the front
                return o;
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float d = i.behind;
                float front = exp(-abs(d - 0.05) / 0.045);
                float trail = exp(-d / max(0.05, 0.4 * _Width)) * (1 - d / max(1e-3, _Width));
                float3 sheen = lerp(float3(1, 1, 1), Hue(i.uv.x * 3 + _Age * 0.8 + d * 0.5), 0.45);
                float3 ice = float3(0.75, 0.9, 1.0);
                float3 rgb = ice * front * 3 + sheen * trail * 0.6;
                return float4(rgb * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
