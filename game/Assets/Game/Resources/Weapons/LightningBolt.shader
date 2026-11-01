// The lightning's bolts (Client/Weapons/LightningView.cs): camera-facing ribbons whose uv.x runs across the
// ribbon (-1 to 1) and uv.y carries the bolt's intensity, coloured by the vertex colour (the
// weapon's). A thin white-hot core, then the weapon's colour glowing out to the ribbon's edges,
// additive and HDR so bloom makes the halo.
Shader "Game/LightningBolt"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            Name "LightningBolt"
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

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 color : TEXCOORD1; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(v.positionOS));
                o.uv = v.uv;
                o.color = v.color.rgb;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float a = abs(i.uv.x);
                float core = exp(-a * a * 160);
                float glow = exp(-a * 3) * (1 - a);
                // A bolt's overbright first frames (intensity past 1) brighten only its core: the
                // whole ribbon overbright blew to white under a slam's flash in crypt (ps-msz.8.11).
                float lit = min(i.uv.y, 1);
                float3 rgb = float3(1, 1, 1) * core * 3 * i.uv.y + i.color * (glow * 2 + core) * lit;
                return float4(rgb, 0);
            }
            ENDHLSL
        }
    }
}
