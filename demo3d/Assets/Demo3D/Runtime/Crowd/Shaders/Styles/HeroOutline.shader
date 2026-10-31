// The hero's glowing outline (crowd.md, The Hero; HeroOutline): an inverted hull round the hero's own
// renderers, so a humanoid-sized hero still reads among a horde of humanoids at the trailers' 45-65 m
// views. Each hero renderer draws its mesh again with it as an extra material (HeroOutline.Attach):
// the back faces pushed out along the normals in clip space, by a constant width in pixels at 1080p, in an HDR colour past
// the bloom's threshold, so the line blooms into a thin halo. It draws after the opaques and the
// blade without writing depth, so the hero's body and blade hide its inner half and only the rim
// round the silhouette shows; it casts no shadow and takes no fog.
Shader "Demo3D/Styles/Shells/HeroOutline"
{
    Properties
    {
        [HDR] _OutlineColor ("Line", Color) = (0, 1.5, 6, 1)
        _WidthPx ("Width (pixels at 1080p)", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "HeroOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor; float _WidthPx;
            CBUFFER_END
            float _Demo3DIdMask;

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };

            float4 vert(Attributes v) : SV_POSITION
            {
                float4 positionCS = TransformObjectToHClip(v.positionOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(v.normalOS)).xy;
                // A pixel is 2 / the screen's size in clip space; the width scales with the output's
                // height, so a 4K capture draws the same line as 1080p.
                float2 px = 2.0 / _ScreenParams.xy * (_ScreenParams.y / 1080.0);
                positionCS.xy += normalize(normalCS + 1e-6) * px * _WidthPx * positionCS.w;
                return positionCS;
            }

            half4 frag() : SV_Target
            {
                if (_Demo3DIdMask > 0.5) return half4(0, 0, 0, 1); // TD15's ID render: the hero draws black
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
