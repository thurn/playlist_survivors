// The stage's ground: a dark, low-contrast cave floor of cracked flagstones, lit by the moon,
// the hero's torch and the crystals, with a faint ring where the beat wave passes.
Shader "StyleLab/Ground"
{
    Properties
    {
        _ColorA ("Stone A", Color) = (0.055, 0.04, 0.065, 1)
        _ColorB ("Stone B", Color) = (0.1, 0.07, 0.09, 1)
        _Crack ("Crack", Color) = (0.015, 0.01, 0.02, 1)
        [HDR] _WaveColor ("Beat ring", Color) = (0.35, 0.25, 0.8, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StyleLabPragmas.hlsl"
            #include "StyleLabCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorA; half4 _ColorB; half4 _Crack; half4 _WaveColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float fogFactor : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 p = i.positionWS;
                float2 vor = SL_Voronoi(float3(p.xz * 0.55, 0.0));
                float big = SL_Fbm(float3(p.xz * 0.08, 1.0));
                float3 albedo = lerp(_ColorA.rgb, _ColorB.rgb, saturate(big * 1.4 - 0.2 + (SL_Noise(float3(p.xz * 0.55, 3.0)) - 0.5) * 0.4));
                albedo *= 0.85 + 0.3 * SL_Noise(float3(p.xz * 3.0, 0.0));
                albedo = lerp(_Crack.rgb, albedo, smoothstep(0.02, 0.07, vor.y));

                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(p);
                Light L = SL_MainLight(p);
                float ao = SL_Ao(i.positionCS);
                float3 color = albedo * (L.color * saturate(dot(N, L.direction)) * L.shadowAttenuation + SampleSH(N) * ao);
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(p, i.positionCS, N, V, 0.0, 30.0, addDiffuse, addSpec);
                color += albedo * addDiffuse * 1.4 + addSpec * albedo * 0.5;

                float d = distance(p.xz, _SL_HeroPos.xz);
                float phase = frac(_SL_Beat - d / max(_SL_WaveSpeed, 0.01));
                float ring = exp(-phase * 70.0) * smoothstep(1.0, 3.0, d) * (1.0 - smoothstep(5.0, 11.0, d));
                color += _WaveColor.rgb * ring * 0.12 * _SL_RingGain;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
