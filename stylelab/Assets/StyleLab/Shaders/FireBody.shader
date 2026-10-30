// Treatment 3, "Living flame", the body: charcoal skin split by glowing cracks (cellular noise
// on the rest position, drifting slowly upward), a molten rim and a heat shimmer. The flames
// themselves are FireShell, drawn by the monster's shell renderer.
Shader "StyleLab/FireBody"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _CrackScale ("Crack scale (per metre)", Float) = 3.5
        _CrackWidth ("Crack width", Float) = 0.07
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
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
            #include "FireCommon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _Ember; half4 _Flame; half4 _Core; half _CrackScale; half _CrackWidth;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                p += n * 0.012 * (SL_Noise(v.rest.xyz * 6.0 + float3(0, -_SL_Time * 4.0, 0)) - 0.5);
                return SL_Finish(v, p, n);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                // Burns away from the feet up as it dies.
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                float burn = _Death * 1.3 - h01 * 0.3;
                clip(SL_Noise(i.rest.xyz * 7.0) - burn);
                float burnEdge = _Death > 0 ? 1.0 - saturate((SL_Noise(i.rest.xyz * 7.0) - burn) / 0.1) : 0.0;

                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = SL_MainLight(i.positionWS);
                float lit = saturate(dot(N, L.direction)) * L.shadowAttenuation;
                float3 charcoal = (0.02 + SL_Luma(tex) * 0.08) * float3(1.0, 0.55, 0.45);
                float3 color = charcoal * (L.color * lit + SampleSH(N) + 0.2);

                float3 q = i.rest.xyz * _CrackScale + float3(0, -_SL_Time * 0.35, 0) + _Seed * 5.0;
                float2 vor = SL_Voronoi(q);
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, vor.y);
                // Only some cracks are live; the rest are cold seams in the charcoal.
                crack *= smoothstep(0.35, 0.65, SL_Noise(i.rest.xyz * 1.3 + float3(0, -_SL_Time * 0.4, 0)));
                float flicker = 0.75 + 0.5 * SL_Noise(i.rest.xyz * 3.0 + _SL_Time * 2.5);
                float wave = SL_Wave(i.positionWS, 6.0);
                // Embers smoulder hottest low on the body, where the flames start.
                float heat = crack * flicker * (0.55 + 0.45 * wave) + pow(1.0 - h01, 3.0) * 0.35;
                color += FireRamp(heat, _Ember.rgb, _Flame.rgb, _Core.rgb);

                float fres = pow(1.0 - saturate(dot(N, V)), 2.5);
                color += FireRamp(0.55 + 0.3 * wave, _Ember.rgb, _Flame.rgb, _Core.rgb) * fres;
                color += _Core.rgb * (_Hit * 0.25 + burnEdge);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
