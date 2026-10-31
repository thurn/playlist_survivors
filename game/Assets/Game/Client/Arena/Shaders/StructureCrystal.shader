// The crystal structures (unity-game.md, "The Look" and "Crystal Structures"): faceted gem refracting
// the camera's opaque texture, its facets world-space cells. demo3d's `--structure-style crystal`
// (its Structures/Crystal.shader and the crowd's CrystalCommon.hlsl under SL_STATIC), copied and
// owned for static meshes. The structures stay out of the bake: the crystal takes no ambient, so it
// is lit by the moon (its realtime shadow and the static shadow map), Forward+'s lights (the
// hero's above all) and what it refracts. Each site has its own gem colour (_SL_Tint,
// StructureStyle's hues across the spectrum). Alpha clip and the canopy fade follow the kit
// material StructureStyle replaces.
Shader "Game/StructureCrystal"
{
    Properties
    {
        [HDR] _Tint ("Gem (a site's _SL_Tint replaces it)", Color) = (0.31, 0.53, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.05
        _Facets ("Faceting", Range(0, 1)) = 0.85
        _Gain ("Structure gain (StructureStyle tuning)", Float) = 0.16
        _Peak ("Structure peak (StructureStyle tuning)", Float) = 0.2
        _HeightM ("Piece height (m), per renderer", Float) = 1.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (the kit material's)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END
        #include "StructureCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            SLVaryings vert(SLAttributes v) { return SL_Finish(v, SL_Skin(v)); }

            // One crystal fragment: faceted gem that refracts what is behind it with a little
            // dispersion, tinted by the site's gem colour, with light trapped in each facet, hard
            // banded reflections, facet edges that catch the light and glints as the beat wave
            // passes. A shard's fresh inside faces glow while it breaks and stay more saturated.
            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i)
                float4 bary = i.bary;
                float hit = SL_HIT(i), inside = SL_INSIDE(i);
                float glow = inside * _SL_PieceFx.w;
                float3 positionWS = i.positionWS;
                float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
                float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                // The facets are cells across the kit's few large triangles: each tilted its own way,
                // as a cut stone's faces are.
                facet = normalize(facet + (SL_Hash3(bary.w * 91.3 + 3.1) - 0.5) * 1.2);
                if (dot(facet, V) < 0) facet = normalize(facet - 1.2 * dot(facet, V) * V);
                float3 N = normalize(lerp(normalize(i.normalWS), facet, _Facets));
                float NdotV = saturate(dot(N, V));

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
                float2 off = nv.xy * _Refraction;
                float3 behind;
                behind.r = SampleSceneColor(suv + off * 1.0).r;
                behind.g = SampleSceneColor(suv + off * 1.2).g;
                behind.b = SampleSceneColor(suv + off * 1.4).b;
                behind *= 0.5; // thick, dark gem: it doesn't uncover the lighter ground behind the horde beside it

                float3 tint = _SL_Tint.a > 0 ? _SL_Tint.rgb : _Tint.rgb;
                float wave = SL_Wave(positionWS, 6.0);
                float faceRand = bary.w;
                float3 color = behind * lerp(float3(1, 1, 1), tint, lerp(0.85, 1.0, inside)) * 1.2 + tint * lerp(0.08, 0.45, inside);
                // Light trapped inside: each facet its own brightness, and a core that throbs on the beat.
                color += tint * (0.1 + 0.85 * faceRand * faceRand) * (0.55 + 1.1 * wave) * (1.0 + 1.5 * inside);
                color += tint * pow(NdotV, 3.0) * 0.35 * (1.0 + wave);

                Light L = SL_MainLight(positionWS);
                float3 R = reflect(-V, N);
                float fres = pow(1.0 - NdotV, 3.0);
                // A cut stone's reflections: hard bands, so neighbouring facets flip between bright and dark.
                float band = smoothstep(0.42, 0.58, frac(dot(R, float3(1.7, 2.3, 1.1)) + faceRand * 0.35));
                float3 env = lerp(float3(0.02, 0.02, 0.05), float3(0.65, 0.7, 1.0), band) * lerp(float3(1, 1, 1), tint, 0.35);
                color += env * (0.3 + fres * 1.3) + tint * fres * 0.7;
                float glint = pow(saturate(dot(R, L.direction)), 300.0) * 25.0 * L.shadowAttenuation;
                color += glint * L.color;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(positionWS, i.positionCS, N, V, 0.0, 250.0, addDiffuse, addSpec);
                color += addSpec * 6.0 + addDiffuse * tint * 0.15;

                // Facet edges catch the light; a few facets flash as each wave passes.
                color += SL_Wire(bary.xyz, 1.0) * (tint * 0.25 + 0.1) * (0.4 + wave);
                float twinkle = step(0.88, SL_Hash1(faceRand * 91.7 + SL_WaveBeat(positionWS) * 13.1));
                color += twinkle * wave * (tint * 3.0 + 1.5);
                color += float3(1.0, 1.0, 1.1) * hit + tint * 3.0 * glow;
                // A fresh break: more saturated than the weathered skin.
                if (inside > 0) color = max(0, lerp(SL_Luma(color).xxx, color, 1.0 + 0.6 * inside));
                return half4(MixFog(SL_Grade(color), i.fogFactor), 1);
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
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            ShadowVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                ShadowVaryings o;
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                o.positionWS = c.positionWS;
                o.uv = v.uv;
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target
            {
                SL_StaticClip(i.positionCS, i.positionWS, i.uv, false);
                return 0;
            }
            ENDHLSL
        }
    }
}
