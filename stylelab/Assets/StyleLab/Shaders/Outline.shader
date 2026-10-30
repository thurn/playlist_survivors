// An inverted-hull ink line, drawn by a monster's shell renderer (Monster.cs) under the
// painted look: the back faces pushed out along the normals, in a dark saturated colour.
Shader "StyleLab/Outline"
{
    Properties
    {
        _OutlineColor ("Line", Color) = (0.05, 0.02, 0.08, 1)
        _Width ("Width (metres)", Float) = 0.025
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "StyleLabCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor; float _Width;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                // Wider with distance, so the line stays about the same on screen.
                float dist = distance(p, GetCameraPositionWS());
                p += n * _Width * (1.0 - _Death) * clamp(dist / 18.0, 0.6, 2.0);
                return SL_Finish(v, p, n);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                clip(0.5 - _Death);
                return half4(MixFog(_OutlineColor.rgb, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
