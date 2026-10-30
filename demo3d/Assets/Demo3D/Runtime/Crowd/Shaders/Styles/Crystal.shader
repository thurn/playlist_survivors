// `--monster-style crystal` (crowd.md, Monster styles): stylelab's "Living crystal" on the
// bone-texture crowd's style mesh. Faceted gemstone bodies drawn after the opaque scene: each
// facet refracts what is behind it (the camera's opaque texture, which MonsterStyle turns on for
// this style only) with a little dispersion, tinted by the type's gem colour. Facet edges catch
// the light, a few facets glint as the beat wave passes, and the core glows on the beat. Death
// shatters it, in every pass.
Shader "Demo3D/Styles/Crystal"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            float _HeightM;
        CBUFFER_END

        #define SL_STYLE_MESH
        #define SL_DISSOLVE(rest, death) 0.0
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) SL_ShatterDisplace(v, m, slot, positionWS, 1.6);
        #include "StyleCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                float shatter = SL_ShatterT(SL_DEATH(i), i.bary.w);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 facet = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                float3 N = normalize(lerp(normalize(i.normalWS), facet, _Facets));
                float NdotV = saturate(dot(N, V));

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
                float2 off = nv.xy * _Refraction;
                float3 behind;
                behind.r = SampleSceneColor(suv + off * 1.0).r;
                behind.g = SampleSceneColor(suv + off * 1.2).g;
                behind.b = SampleSceneColor(suv + off * 1.4).b;

                float3 tint = _Tint.rgb;
                float wave = SL_Wave(i.positionWS, 6.0);
                float faceRand = i.bary.w;
                float3 color = behind * lerp(float3(1, 1, 1), tint, 0.85) * 1.2 + tint * 0.08;
                // Light trapped inside: each facet its own brightness, like a cut stone, and a core
                // that throbs on the beat.
                color += tint * (0.1 + 0.85 * faceRand * faceRand) * (0.55 + 1.1 * wave);
                color += tint * pow(NdotV, 3.0) * 0.35 * (1.0 + wave);

                Light L = SL_MainLight(i.positionWS);
                float3 R = reflect(-V, N);
                float fres = pow(1.0 - NdotV, 3.0);
                // A cut stone's reflections: hard bands, so neighbouring facets flip between bright and dark.
                float band = smoothstep(0.42, 0.58, frac(dot(R, float3(1.7, 2.3, 1.1)) + faceRand * 0.35));
                float3 env = lerp(float3(0.02, 0.02, 0.05), float3(0.65, 0.7, 1.0), band) * lerp(float3(1, 1, 1), tint, 0.35);
                color += env * (0.3 + fres * 1.3) + tint * fres * 0.7;
                float glint = pow(saturate(dot(R, L.direction)), 300.0) * 25.0 * L.shadowAttenuation;
                color += glint * L.color;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 250.0, addDiffuse, addSpec);
                color += addSpec * 6.0 + addDiffuse * tint * 0.15;

                // Facet edges catch the light; a few facets flash as each wave passes.
                color += SL_Wire(i.bary.xyz, 1.0) * (tint * 0.25 + 0.1) * (0.4 + wave);
                float twinkle = step(0.88, SL_Hash1(faceRand * 91.7 + SL_WaveBeat(i.positionWS) * 13.1));
                color += twinkle * wave * (tint * 3.0 + 1.5);
                color += float3(1.0, 1.0, 1.1) * SL_HIT(i) + tint * 3.0 * shatter;
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
