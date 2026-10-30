// `--monster-style fire` (crowd.md, Monster styles), the body: stylelab's "Living flame".
// Charcoal skin split by glowing cracks (cellular noise on the rest position, drifting slowly
// upward), a molten rim and a heat shimmer, in the type's fire colours. It burns away from the
// feet up as it dies. The flames are FireShell, the type's shell draw. The shimmer and the burn
// are its SL_DISPLACE and SL_DISSOLVE, so its shadows, depth and motion vectors follow them.
Shader "Demo3D/Styles/FireBody"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _CrackScale ("Crack scale (per metre)", Float) = 3.5
        _CrackWidth ("Crack width", Float) = 0.07
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor;
            half4 _Ember; half4 _Flame; half4 _Core; half _CrackScale; half _CrackWidth;
            float _HeightM;
        CBUFFER_END

        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
            positionWS += normalWS * 0.012 * (SL_Noise((v).positionOS * 6.0 + float3(0, -_SL_Time * 4.0, 0)) - 0.5);
        #define SL_DISSOLVE(rest, death) FireBurn(rest, death)
        #include "StyleCommon.hlsl"
        #include "FireCommon.hlsl"

        // Burns away from the feet up through noise on the rest position. Returns the hot edge.
        float FireBurn(float3 rest, float death)
        {
            if (death <= 0) return 0;
            float h01 = saturate(rest.y / max(_HeightM, 0.1));
            float burn = death * 1.3 - h01 * 0.3;
            float d = SL_Noise(rest * 7.0) - burn;
            clip(d);
            return 1.0 - saturate(d / 0.1);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, burnEdge)
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = SL_MainLight(i.positionWS);
                float lit = saturate(dot(N, L.direction)) * L.shadowAttenuation;
                float3 charcoal = (0.02 + SL_Luma(tex) * 0.08) * float3(1.0, 0.55, 0.45);
                float3 color = charcoal * (L.color * lit + SampleSH(N) + 0.2);

                float3 q = i.rest * _CrackScale + float3(0, -_SL_Time * 0.35, 0) + SL_SEED(i) * 5.0;
                float2 vor = SL_Voronoi(q);
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, vor.y);
                // Only some cracks are live; the rest are cold seams in the charcoal.
                crack *= smoothstep(0.35, 0.65, SL_Noise(i.rest * 1.3 + float3(0, -_SL_Time * 0.4, 0)));
                float flicker = 0.75 + 0.5 * SL_Noise(i.rest * 3.0 + _SL_Time * 2.5);
                float wave = SL_Wave(i.positionWS, 6.0);
                // Embers smoulder hottest low on the body, where the flames start.
                float heat = crack * flicker * (0.55 + 0.45 * wave) + pow(1.0 - h01, 3.0) * 0.35;
                color += FireRamp(heat, _Ember.rgb, _Flame.rgb, _Core.rgb);

                float fres = pow(1.0 - saturate(dot(N, V)), 2.5);
                color += FireRamp(0.55 + 0.3 * wave, _Ember.rgb, _Flame.rgb, _Core.rgb) * fres;
                color += _Core.rgb * (SL_HIT(i) * 0.25 + burnEdge);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define SL_PASS_SHADOWCASTER
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_DEPTHONLY
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define SL_PASS_DEPTHNORMALS
            #include "StylePasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ZWrite On
            ColorMask RG
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_MOTIONVECTORS
            #include "StylePasses.hlsl"
            ENDHLSL
        }
    }
}
