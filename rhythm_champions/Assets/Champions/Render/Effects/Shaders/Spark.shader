// The hero's sparks (Sparks.cs, Sparks.compute): each a streak from where it was a moment ago to where it is now, a
// constant width on screen, white-hot and cooling to the faint white-blue edge as it dies. One procedural draw for every
// live spark: instance i is the ring's slot _First + i, six vertices a quad. Additive in HDR; an effect's code in the
// stills' type-ID pass.
Shader "Champions/Spark"
{
    Properties
    {
        _CoreColor ("Core (linear)", Color) = (1, 1, 1, 1)
        _EdgeColor ("Edge (linear)", Color) = (0.77, 0.85, 1, 1)
        _ChampionsId ("Type-ID pass code", Float) = 252
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Spark"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            #include "Effects.hlsl"

            #define TRAIL_S 0.035 // the streak spans this much of its flight
            #define MIN_PX 1.6    // the narrowest a spark draws, in pixels at 1080 rows

            StructuredBuffer<Spark> _Sparks;
            uint _First, _Capacity;
            half4 _CoreColor, _EdgeColor;
            float _ChampionsId;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 quad : TEXCOORD0;     // along (0 tail, 1 head), across (-1 to 1)
                nointerpolation float heat : TEXCOORD1;
                nointerpolation float gain : TEXCOORD2;
            };

            Varyings vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Spark s = _Sparks[(_First + instanceID) % _Capacity];
                float age = _EffectNow - s.born;
                Varyings o;
                o.quad = 0;
                o.heat = 0;
                o.gain = 0;
                if (age < 0 || age > s.life)
                {
                    o.positionCS = float4(0, 0, -2, 1); // outside the clip volume: nothing drawn
                    return o;
                }
                float3 head = SparkAt(s, age), tail = SparkAt(s, max(0, age - TRAIL_S));
                float4 h = TransformWorldToHClip(head), t = TransformWorldToHClip(tail);
                float2 halfScreen = 0.5 * _ScaledScreenParams.xy;
                float2 d = h.xy / h.w * halfScreen - t.xy / t.w * halfScreen;
                float len = length(d);
                d = len > 1e-3 ? d / len : float2(1, 0);
                // Its width in metres at its distance, at least MIN_PX.
                float px = max(MIN_PX * _ScaledScreenParams.y / 1080.0, s.size * UNITY_MATRIX_P[1][1] * halfScreen.y / h.w);
                uint corner = vertexID % 6;
                float along = (corner == 1 || corner == 2 || corner == 4) ? 1 : 0;
                float side = (corner == 2 || corner == 4 || corner == 5) ? 1 : -1;
                float4 c = along > 0 ? h : t;
                // Half a width past each end, so a slow spark is a dot rather than nothing.
                c.xy += (float2(-d.y, d.x) * side * px * 0.5 + d * (along > 0 ? 0.5 : -0.5) * px) / halfScreen * c.w;
                o.positionCS = c;
                o.quad = float2(along, side);
                float life = age / s.life;
                o.heat = 1 - life;
                o.gain = s.brightness * (1 - life) * (1 - life);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float lit = (1 - i.quad.y * i.quad.y) * lerp(0.35, 1, i.quad.x) * i.gain;
                if (_ChampionsIdPass > 0.5) clip(lit - 0.08);
                CHAMPIONS_ID_PASS(_ChampionsId)
                half3 colour = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(i.heat * 1.4));
                return half4(colour * lit, 1);
            }
            ENDHLSL
        }
    }
}
