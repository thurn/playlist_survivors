// The painted treatment's ink line (crowd.md, Monster styles): stylelab's inverted hull, drawn as
// the type's shell (MonsterStyle.Shells), a second indirect draw on the same instances. The back
// faces are pushed out along the normals, wider with distance so the line stays about the same on
// screen, in a dark saturated colour. The push is its SL_DISPLACE, so its depth and motion vectors
// match it; it casts no shadow (the body does).
Shader "Demo3D/Styles/Shells/Outline"
{
    Properties
    {
        _OutlineColor ("Line", Color) = (0.05, 0.02, 0.08, 1)
        _Width ("Width (metres)", Float) = 0.025
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor; float _Width;
            float _HeightM;
        CBUFFER_END

        #define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
            positionWS += normalWS * _Width * (1.0 - (m).dissolve) * \
                clamp(distance(positionWS, GetCameraPositionWS()) / 18.0, 0.6, 2.0);
        #define SL_DISSOLVE(rest, death) OutlineDeath(rest, death)
        #include "StyleCommon.hlsl"

        // The lab's line goes at half death; the body's dissolve clips it before then too.
        float OutlineDeath(float3 rest, float death)
        {
            clip(0.5 - death);
            return SL_Dissolve(rest, death);
        }
        ENDHLSL

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
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

            half4 frag(SLVaryings i) : SV_Target
            {
                SL_FRAGMENT_BEGIN(i, edge)
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Front
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
            Cull Front
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
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #define SL_PASS_MOTIONVECTORS
            #include "StylePasses.hlsl"
            ENDHLSL
        }
    }
}
