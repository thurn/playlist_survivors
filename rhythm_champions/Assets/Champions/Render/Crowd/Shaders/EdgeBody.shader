// An edge cut's dark body (design.md, The Look > Edge cuts): the near-black occluder that hides the lines behind
// it, drawn from both sides (its parts are closed hulls and flat discs). A hit lifts it a little toward the
// monster's tint. EDGE_SHARD draws the shatter's pieces (EdgeCommon.hlsl). Under _EdgeIdMask, flat white.
Shader "Champions/EdgeBody"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "EdgeBody"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ EDGE_SHARD
            #include "EdgeCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 boneIndex : TEXCOORD1;
                float4 boneWeight : TEXCOORD2;
                float4 piece : TEXCOORD3;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation half3 color : TEXCOORD0;
                float heightWS : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Owner o = OwnerOf(v.instanceID);
                float3 w = Place(o, v.positionOS, v.boneIndex, v.boneWeight, v.piece.x);
                Varyings r;
                r.positionCS = TransformWorldToHClip(w);
                r.color = _EdgeIdMask > 0.5 ? 1 : _EdgeBody.rgb + o.tint * (_EdgeBody.a * o.hitFlash);
                r.heightWS = w.y;
                return r;
            }

            half4 frag(Varyings i) : SV_Target
            {
            #ifdef EDGE_SHARD
                clip(i.heightWS); // a sinking piece goes under the floor
            #endif
                return half4(i.color, 1);
            }
            ENDHLSL
        }
    }
}
