// Glowing rune circles on the ground (TD15; environment.md, Mood: cyan and violet magic from
// glowing trees, crystals and runes): our own procedural ring of glyph dashes, two thin rings and
// inner spokes, turning slowly and breathing, drawn additively a few centimetres above the ground
// so bloom picks it up. Every circle is one quad of one merged mesh (ArenaDressing; vertex colour
// is the circle's tint and intensity in alpha). Nothing vendor-derived.
Shader "Demo3D/RuneCircle"
{
    Properties
    {
        _Turn("Turns per second", Float) = 0.01
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Runes"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float _Turn;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv * 2 - 1;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Band(float r, float at, float w) { return saturate(1 - abs(r - at) / w); }

            float Hash(float x) { return frac(sin(x * 91.345) * 47453.5453); }

            float _Demo3DTime;

            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                if (r > 1) discard;
                float a = atan2(i.uv.y, i.uv.x) / (2 * PI) + 0.5;           // 0..1 round the circle
                float t = _Demo3DTime; // ArenaClock: song time in the demo, Time.time elsewhere
                // Outer glyph band: 48 dashes of hashed lengths, turning one way.
                float ao = frac(a + t * _Turn);
                float cell = floor(ao * 48), f = frac(ao * 48);
                float glyph = step(0.18, f) * step(f, 0.18 + 0.6 * Hash(cell + 1)) * step(0.25, Hash(cell + 7));
                float g = glyph * saturate(1 - abs(r - 0.885) / 0.035);
                // Two thin rings either side of it, and an inner ring.
                float rings = Band(r, 0.955, 0.012) + Band(r, 0.815, 0.010) + 0.7 * Band(r, 0.52, 0.012);
                // Inner spokes, turning the other way, fading toward the centre.
                float ai = frac(a - t * _Turn * 1.7);
                float spokes = Band(frac(ai * 12), 0.5, 0.035) * smoothstep(0.2, 0.5, r) * step(r, 0.8) * 0.3;
                float breathe = 0.8 + 0.2 * sin(t * 1.3 + r * 3.0);
                float m = (rings + g + spokes) * breathe;
                float3 c = i.color.rgb * i.color.a * 4.0 * m;               // alpha: intensity (x4 headroom)
                c = MixFogColor(c, 0, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
