// Treatment 1, "Demo3D today": the crowd shader's current look (CrowdSkinned.shader), for
// comparison. The vendor albedo through URP's PBR lighting, smoothness 0.2, the night's cool
// rim multiplied by the albedo, and the dissolve with a hot edge on death.
Shader "StyleLab/Baseline"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _RimColor ("Rim (TD15)", Color) = (0.32, 0.42, 0.75, 1)
        _Fill ("Ambient fill (TD15)", Float) = 0.35
        [HDR] _EdgeColor ("Dissolve Edge", Color) = (6, 2.2, 0.6, 1)
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor; half _Smoothness; half4 _RimColor; half _Fill; half4 _EdgeColor;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                return SL_Finish(v, TransformObjectToWorld(v.positionOS.xyz), TransformObjectToWorldNormal(v.normalOS));
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                float edge = SL_Dissolve(i.rest.xyz, _Death);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(d.normalWS) * (1 + _Fill);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = 1;
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.smoothness = _Smoothness;
                s.occlusion = 1;
                s.alpha = 1;
                half rim = 1 - saturate(dot(d.normalWS, d.viewDirectionWS));
                s.emission = _RimColor.rgb * (rim * rim * rim) * albedo * 4
                    + float3(2.5, 2.4, 2.2) * _Hit + _EdgeColor.rgb * edge;
                half3 color = UniversalFragmentPBR(d, s).rgb;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
