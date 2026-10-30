// `--monster-style void` (crowd.md, Monster styles): stylelab's "Void", creatures cut out of the
// night sky. The body shows a starfield and a faint nebula fixed to the screen, so each monster
// looks like a hole into space, ringed by an intense coloured edge that flares as the beat wave
// passes. Death implodes it to a point at its middle (its SL_DISPLACE, so shadows, depth and
// motion vectors follow) instead of dissolving.
Shader "Demo3D/Styles/Void"
{
    Properties
    {
        _BaseMap ("Albedo (unused)", 2D) = "white" {}
        _BaseColor ("Color (unused)", Color) = (1, 1, 1, 1)
        [HDR] _RimColor ("Edge", Color) = (0.6, 0.3, 3.0, 1)
        _RimPower ("Edge tightness", Float) = 4
        _NebulaA ("Nebula A", Color) = (0.25, 0.05, 0.4, 1)
        _NebulaB ("Nebula B", Color) = (0.0, 0.3, 0.45, 1)
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
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
            half4 _RimColor; half _RimPower; half4 _NebulaA; half4 _NebulaB;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END

        // _HeightM is at instance scale 1; the instance's scale is in its basis (CrowdSphere's height).
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
        { \
            float implode = smoothstep(0.0, 1.0, (m).dissolve); \
            float3 middle = (m).position + float3(0, _HeightM * 0.5 * (m).scale, 0); \
            positionWS = lerp(positionWS, middle, implode * implode); \
        }
        #define SL_DISSOLVE(rest, death) 0.0
        #include "StyleCommon.hlsl"
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
            #pragma multi_compile_fog

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            float Stars(float2 p, float scale, float seed)
            {
                float2 q = p * scale;
                float2 cell = floor(q);
                float2 f = frac(q) - 0.5;
                float h = SL_Hash(float3(cell, seed));
                float2 at = (SL_Hash3(float3(cell, seed + 1.0)).xy - 0.5) * 0.7;
                float size = 0.04 + 0.08 * SL_Hash(float3(cell, seed + 2.0));
                float twinkle = 0.6 + 0.4 * sin(_SL_Time * (2.0 + h * 5.0) + h * 40.0);
                return step(0.72, h) * smoothstep(size, 0.0, length(f - at)) * twinkle;
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
                float2 p = suv * float2(_ScreenParams.x / _ScreenParams.y, 1.0) + nv.xy * 0.02 + SL_SEED(i) * 3.7;
                float wave = SL_Wave(i.positionWS, 6.0);

                float stars = Stars(p, 40.0, 1.0) + Stars(p, 90.0, 7.0) * 0.6 + Stars(p, 18.0, 3.0) * 1.5;
                float n = SL_Fbm(float3(p * 3.0, _SL_Time * 0.05));
                float3 nebula = lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(n * 1.6 - 0.3)) * pow(saturate(n), 2.2) * 0.9;
                float3 color = nebula + stars * (2.2 + wave * 2.5);

                float rim = pow(1.0 - NdotV, _RimPower);
                color += _RimColor.rgb * rim * (0.9 + 2.5 * wave + SL_DEATH(i) * 6.0);
                color += _RimColor.rgb * smoothstep(0.78, 0.86, 1.0 - NdotV) * 0.35;
                color += float3(1.0, 1.0, 1.0) * SL_HIT(i) * rim;
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
