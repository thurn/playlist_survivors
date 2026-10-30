// `--monster-style chrome` (crowd.md, Monster styles): stylelab's "Liquid chrome". A mirror
// finish reflecting a stylized synthwave world that is computed, not sampled: a sunset sky with a
// striped sun, studio light strips overhead and a neon grid floor. A ripple runs over the surface
// on the beat. Death melts it into a puddle round its feet, which then dissolves. The ripple and
// the melt are its SL_DISPLACE, so its shadows, depth and motion vectors follow them.
Shader "Demo3D/Styles/Chrome"
{
    Properties
    {
        _BaseMap ("Albedo (unused)", 2D) = "white" {}
        _BaseColor ("Color (unused)", Color) = (1, 1, 1, 1)
        _Tint ("Metal tint", Color) = (1, 1, 1, 1)
        _Ripple ("Ripple (metres)", Float) = 0.012
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
            half4 _Tint; float _Ripple;
            float _HeightM;
        CBUFFER_END

        // The ripple, then the melt: everything sinks to a puddle that spreads out round the feet.
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
        { \
            positionWS += normalWS * _Ripple * sin((v).positionOS.y * 16.0 - _SL_Time * 7.0 + SL_SeedOf(slot) * 6.28) * (0.4 + 0.8 * _SL_Pulse); \
            float melt = smoothstep(0.0, 1.0, (m).dissolve); \
            float groundY = (m).position.y + 0.02; \
            positionWS.y = lerp(positionWS.y, groundY + (positionWS.y - groundY) * 0.04, melt); \
            positionWS.xz += (positionWS.xz - (m).position.xz) * melt * 0.9; \
        }
        // The puddle lingers, then the baseline's dissolve takes it in the last third of the death.
        #define SL_DISSOLVE(rest, death) SL_Dissolve(rest, saturate((death) * 3.0 - 2.0))
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
            #include_with_pragmas "StylePragmas.hlsl"

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            float3 SynthwaveWorld(float3 R)
            {
                // Turn slowly, so the reflections drift.
                float a = _SL_Time * 0.08;
                float s = sin(a), c = cos(a);
                R.xz = float2(c * R.x - s * R.z, s * R.x + c * R.z);
                float y = R.y;
                float3 color;
                if (y >= 0.0)
                {
                    float3 horizon = float3(1.8, 0.55, 0.35);
                    float3 mid = float3(0.6, 0.08, 0.55);
                    float3 top = float3(0.05, 0.02, 0.16);
                    color = lerp(horizon, mid, smoothstep(0.0, 0.22, y));
                    color = lerp(color, top, smoothstep(0.22, 0.75, y));
                    float3 sunDir = normalize(float3(0.0, 0.16, 1.0));
                    float sunD = dot(R, sunDir);
                    float stripes = step(0.45, frac((y - 0.16) * 38.0)) + step(0.16, y);
                    color += smoothstep(0.975, 0.982, sunD) * saturate(stripes) * lerp(float3(5, 1.2, 1.6), float3(6, 4.5, 1), saturate((y - 0.04) * 6.0));
                    // Two thin studio strips overhead, so the monsters' tops catch long highlights.
                    float strip = smoothstep(0.02, 0.0, abs(R.x - 0.3)) + smoothstep(0.012, 0.0, abs(R.x + 0.45)) * 0.7;
                    color += strip * smoothstep(0.45, 0.7, y) * 1.4;
                }
                else
                {
                    float2 g = R.xz / max(-y, 0.04) * 1.5;
                    float2 gf = abs(frac(g) - 0.5);
                    float grid = smoothstep(0.44, 0.5, max(gf.x, gf.y));
                    color = float3(0.012, 0.004, 0.03) + grid * float3(0.25, 1.1, 2.0) * saturate(-y * 4.0);
                    color += float3(1.2, 0.3, 0.7) * exp(y * 14.0) * 0.7;
                }
                return color;
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float3 R = reflect(-V, N);
                Light L = SL_MainLight(i.positionWS);
                float ao = SL_Ao(i.positionCS);
                float wave = SL_Wave(i.positionWS, 6.0);

                float reflectance = 0.7 + 0.3 * pow(1.0 - NdotV, 4.0);
                float3 color = SynthwaveWorld(R) * _Tint.rgb * reflectance * (0.85 + 0.5 * wave);
                color *= lerp(0.5, 1.0, L.shadowAttenuation) * lerp(0.35, 1.0, ao);
                float3 H = normalize(L.direction + V);
                color += pow(saturate(dot(N, H)), 400.0) * 18.0 * L.color * L.shadowAttenuation;
                float3 addDiffuse = 0, addSpec = 0;
                SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 300.0, addDiffuse, addSpec);
                color += addSpec * 10.0 * _Tint.rgb + addDiffuse * _Tint.rgb * 0.08;
                color += float3(1.0, 0.95, 0.9) * SL_HIT(i);
                color += float3(1.8, 0.55, 0.35) * 2.0 * edge;
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
