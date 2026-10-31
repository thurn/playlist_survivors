// Low mist cards in the arena's hollows (demo3d's TD15; docs/tech-demo-3d/environment.md, Atmosphere): flat, horizontal
// cards a little above the ground, alpha-blended in a cool tint, broken up by two octaves of
// slowly drifting value noise in world space, with an elliptical edge (from the card's uv) and a
// soft depth fade where they meet the ground, props or monsters. Every card is one mesh (the
// baker merges them; the vertex colour's alpha is each card's opacity), so the mist is one draw.
// Our own shader; nothing vendor-derived.
Shader "Game/MistCard"
{
    Properties
    {
        [HDR] _MistColor("Mist colour (linear)", Color) = (0.16, 0.22, 0.32, 1)
        _NoiseScale("Noise scale (1/m)", Float) = 0.12
        _Drift("Drift (m/s, xz)", Vector) = (0.25, 0.08, 0, 0)
        _SoftM("Depth softness (m)", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Mist"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _MistColor;
            float _NoiseScale;
            float4 _Drift;
            float _SoftM;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float opacity : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = v.uv;
                o.opacity = v.color.a;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x),
                            lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            float _GameTime;

            half4 frag(Varyings i) : SV_Target
            {
                float t = _GameTime; // ArenaClock: fixed time in stills, Time.time elsewhere
                float2 w = i.positionWS.xz * _NoiseScale;
                float n = 0.65 * Noise(w + _Drift.xy * _NoiseScale * t)
                        + 0.35 * Noise(w * 2.7 - _Drift.yx * _NoiseScale * 1.9 * t + 17.3);
                float2 e = i.uv * 2 - 1;
                float edge = saturate(1 - dot(e, e));
                edge = edge * edge * edge;
                float density = edge * smoothstep(0.35, 0.9, n);

                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                density *= saturate((scene - self) / _SoftM);

                half4 c = half4(_MistColor.rgb, saturate(density * i.opacity));
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return c;
            }
            ENDHLSL
        }
    }
}
