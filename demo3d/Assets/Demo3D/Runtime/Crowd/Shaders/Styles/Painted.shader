// `--monster-style painted` (crowd.md, Monster styles): stylelab's "Polished painted", the Deep
// Rock Galactic: Survivor look, on the bone-texture crowd. The albedo's colours are thrown away
// and only its light and dark kept, looked up in a colour strip per type. Shadows turn a deeper,
// saturated hue instead of grey; a hard highlight and a lacquered sheen (an analytic matcap)
// make the shell glossy; a complementary rim lifts the silhouette off the ground; glowing spots
// pulse as the beat wave passes. Depth, shadows and motion vectors are CrowdSkinned's passes.
Shader "Demo3D/Styles/Painted"
{
    Properties
    {
        _BaseMap ("Albedo (only its value is used)", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Ramp ("Type colour strip", 2D) = "white" {}
        _LumRange ("Albedo value range (min, max)", Vector) = (0.05, 0.6, 0, 0)
        _ShadowColor ("Shadow tint", Color) = (0.35, 0.2, 0.6, 1)
        [HDR] _RimColor ("Rim", Color) = (1.5, 0.6, 0.2, 1)
        [HDR] _SpecColor ("Highlight", Color) = (1.4, 1.35, 1.25, 1)
        _Gloss ("Highlight tightness", Float) = 48
        _Sheen ("Sheen", Range(0, 2)) = 0.8
        [HDR] _SpotColor ("Glow spots", Color) = (0.2, 2.5, 2.2, 1)
        _SpotScale ("Glow spot scale (per metre)", Float) = 5
        _SpotAmount ("Glow spot threshold", Range(0, 1)) = 0.68
        _Ambient ("Ambient", Float) = 0.9
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor;
                float4 _LumRange; half4 _ShadowColor; half4 _RimColor; half4 _SpecColor; half _Gloss;
                half _Sheen; half4 _SpotColor; half _SpotScale; half _SpotAmount; half _Ambient;
                float _HeightM;
            CBUFFER_END

            #include "StyleCommon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Ramp); SAMPLER(sampler_Ramp);

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            // A lacquer reflection that doesn't need a cubemap: a soft studio gradient by the
            // view-space normal, plus one bright window shape up and to the left.
            float3 Sheen(float3 N)
            {
                float3 vn = normalize(mul((float3x3)UNITY_MATRIX_V, N));
                float sky = saturate(vn.y * 0.5 + 0.5);
                float3 studio = lerp(float3(0.02, 0.015, 0.04), float3(0.75, 0.75, 0.9), sky * sky * sky);
                float2 w = (vn.xy - float2(-0.38, 0.42)) * float2(1.0, 1.7);
                float window = smoothstep(0.24, 0.16, length(w));
                float strip = smoothstep(0.05, 0.0, abs(vn.x + vn.y * 0.35 - 0.42)) * saturate(vn.z);
                return studio * 0.35 + window * 1.3 + strip * 0.35;
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                half3 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float t = saturate((SL_Luma(tex) - _LumRange.x) / max(_LumRange.y - _LumRange.x, 1e-3));
                half3 albedo = SAMPLE_TEXTURE2D(_Ramp, sampler_Ramp, float2(t * 0.9 + 0.05, 0.5)).rgb;

                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = SL_MainLight(i.positionWS);
                float nl = dot(N, L.direction);
                // A soft cel terminator, and shadows that keep their colour.
                float lit = smoothstep(-0.05, 0.3, nl) * L.shadowAttenuation;
                float ao = SL_Ao(i.positionCS);
                float3 shadowSide = albedo * _ShadowColor.rgb;
                float3 color = lerp(shadowSide, albedo * L.color * 1.15 + shadowSide * 0.25, lit);
                color += albedo * SampleSH(N) * _Ambient;
                color *= lerp(0.45, 1.0, ao);

                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.35, _Gloss * 1.5, addDiffuse, addSpec);
                color += albedo * addDiffuse * 1.2 + smoothstep(0.35, 0.6, addSpec) * _SpecColor.rgb * 0.8;

                float3 H = normalize(L.direction + V);
                float spec = pow(saturate(dot(N, H)), _Gloss);
                color += smoothstep(0.42, 0.55, spec) * _SpecColor.rgb * L.color * L.shadowAttenuation;

                float NdotV = saturate(dot(N, V));
                float fres = pow(1.0 - NdotV, 2.0);
                color += Sheen(N) * _Sheen * (0.3 + 0.7 * fres) * lerp(0.5, 1.0, ao);

                float wave = SL_Wave(i.positionWS, 7.0);
                color += _RimColor.rgb * pow(1.0 - NdotV, 3.0) * (0.7 + 1.6 * wave) * lerp(0.6, 1.0, lit);

                float n = SL_Noise(i.rest * _SpotScale + SL_SEED(i) * 13.0);
                float spot = smoothstep(_SpotAmount, _SpotAmount + 0.05, n);
                color += spot * _SpotColor.rgb * (0.5 + 3.0 * wave);

                color += float3(1.5, 1.25, 1.0) * SL_HIT(i);
                color += _RimColor.rgb * 3.0 * edge;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Demo3D/CrowdSkinned/SHADOWCASTER"
        UsePass "Demo3D/CrowdSkinned/DEPTHONLY"
        UsePass "Demo3D/CrowdSkinned/DEPTHNORMALS"
        UsePass "Demo3D/CrowdSkinned/MOTIONVECTORS"
    }
}
