// POLYGON CyberCity's hologram material (SyntyStudios_Hologram_01, an Amplify surface shader the
// URP converter can't draw) ported for the neon environment's hologram trees (ps-bop.3.7;
// environment.md, The kit). As the original: the neon colour through the emission mask, dimmed to
// 70% between scan lines that scroll up the piece in world height, at the material's power and
// opacity. Drawn additively (its faded alpha at 0.08 opacity was the same light on black), unlit,
// both faces, no depth write, so it never shades the horde or enters the static shadow map.
Shader "Demo3D/KitHologram"
{
    Properties
    {
        _BaseMap("Albedo (alpha: coverage)", 2D) = "white" {}
        _EmissionMap("Emission mask", 2D) = "white" {}
        _LinesMap("Scan lines", 2D) = "white" {}
        [HDR] _EmissionColor("Neon (x power)", Color) = (1, 0, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 0.8
        _ScrollSpeed("Scan lines per second", Float) = 0.1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_LinesMap); SAMPLER(sampler_LinesMap);

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _EmissionColor;
            float _Opacity;
            float _ScrollSpeed;
            CBUFFER_END

            float _Demo3DTime; // ArenaClock: song time in the demo, Time.time elsewhere

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float worldY : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                o.worldY = p.positionWS.y;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float coverage = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _Opacity;
                float3 neon = _EmissionColor.rgb * SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb;
                float lines = SAMPLE_TEXTURE2D(_LinesMap, sampler_LinesMap, (i.worldY + _Demo3DTime * _ScrollSpeed).xx).r;
                float3 c = neon * lerp(0.7, 1.0, lines) * coverage;
                c = MixFogColor(c, 0, i.fogFactor);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
