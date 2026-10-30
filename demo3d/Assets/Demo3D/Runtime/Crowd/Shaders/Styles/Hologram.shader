// `--monster-style hologram` (crowd.md, Monster styles): stylelab's "Hologram" on the
// bone-texture crowd's style mesh. See-through projections, additive (so unsorted), with a strong
// fresnel edge, fine scanlines, a bright band rolling up the body, a dim wireframe, red and cyan
// fringes on opposite edges, flicker, and horizontal glitch slices on the off-beats. Death scans
// it out from the top down. A projection of light casts no shadow, so it has no ShadowCaster;
// its depth and motion passes, kept like every style's, are skipped in the transparent queue.
Shader "Demo3D/Styles/Hologram"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (0.2, 1.4, 2.0, 1)
        _Glitch ("Glitch", Float) = 0.12
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color; float _Glitch;
            float _HeightM;
        CBUFFER_END

        // The scan-out: everything above a line falling from the head is cut.
        #define SL_STYLE_MESH
        #define SL_DISSOLVE(rest, death) HologramScanOut(rest, death)
        // Glitch slices: just after each off-beat (and while dying), a few horizontal slices jump sideways.
        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
            { \
                float active = step(frac(_SL_Beat + 0.5), 0.14) + m.dissolve; \
                float r = SL_Hash1(floor(v.positionOS.y * 7.0) + floor(_SL_Time * 18.0) + SL_SeedOf(slot) * 31.0); \
                float3 side = normalize(cross(float3(0, 1, 0), GetWorldSpaceNormalizeViewDir(positionWS)) + 1e-4); \
                positionWS += side * step(0.75, r) * (r - 0.75) * 4.0 * _Glitch * saturate(active); \
            }
        #include "StyleCommon.hlsl"

        // Clips above the scan line; returns its bright edge.
        float HologramScanOut(float3 rest, float death)
        {
            if (death <= 0.0) return 0.0;
            float h01 = saturate(rest.y / max(_HeightM, 0.1));
            float cut = 1.0 - death * 1.1;
            clip(cut - h01);
            return smoothstep(0.06, 0.0, cut - h01);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                return SL_Finish(v, c, c.positionWS, c.normalWS);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, scanEdge)
                float h = i.rest.y;
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = pow(1.0 - saturate(dot(N, V)), 1.6);
                float scan = 0.55 + 0.45 * sin(i.positionWS.y * 110.0 - _SL_Time * 8.0);
                float roll = exp(-abs(frac(h * 0.45 - _SL_Time * 0.55) - 0.5) * 18.0);
                float flicker = 0.82 + 0.18 * SL_Hash1(floor(_SL_Time * 24.0) + SL_SEED(i) * 7.0);
                float wave = SL_Wave(i.positionWS, 6.0);
                float wire = SL_Wire(i.bary.xyz, 1.2);

                float3 color = _Color.rgb * ((0.05 + fres * 1.1) * scan + roll * 0.5 + wire * 0.3);
                float3 vn = mul((float3x3)UNITY_MATRIX_V, N);
                color *= float3(1.0 + vn.x * 0.9, 1.0, 1.0 - vn.x * 0.9);
                color *= flicker * (0.75 + 1.0 * wave);
                color += float3(1.5, 1.5, 1.5) * SL_HIT(i) * fres + _Color.rgb * 4.0 * scanEdge;
                // Added light fades into the fog rather than glowing through it.
                return half4(MixFogColor(color, half3(0, 0, 0), i.fogFactor), 0);
            }
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
