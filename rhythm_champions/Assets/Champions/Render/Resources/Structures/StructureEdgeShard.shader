// A broken structure's shards (design.md, The Look > Shatter: "structures break the same way in their fill colour"):
// its chunks (StructureType.ShardMesh) moved by their PhysX bodies (StructureShatter), in the standing body's surface
// (StructureCommon.hlsl): the zone's fill under the zone's edge lines, every chunk outlined where it broke (the shard
// baker's crack edges) as well as on the model's edges. The outlines are hottest on the break's frame, fading over
// FRESH_SECONDS as the monsters' shards' do, and the set flashes on the impact frame; the pieces fade as they sink. In
// the stills' type-ID pass it writes the effects' code.
Shader "Champions/StructureEdgeShard"
{
    Properties
    {
        _Fill ("Fill gain (over the zone's fill)", Float) = 0.5
        _Edge ("Edge gain (over the zone's edge)", Float) = 3
        _EdgePx ("Edge width (px at 2160 rows, the trailer's capture)", Float) = 1.25
        _Beat ("Edge lift on the break's frame", Float) = 2.5
        _Flash ("The break's lift toward the edge colour", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Fill; half _Edge; half _EdgePx; half _Beat; half _Flash;
            CBUFFER_END
            #include "Assets/Champions/Render/Structures/Shaders/StructureCommon.hlsl"

            #define FRESH_SECONDS 0.6

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 bary : TEXCOORD3;
                float2 chunk : TEXCOORD4; // x 1 on an inside face, y the chunk's index
                float3 edges : TEXCOORD5; // the triangle's edges and its chunk's crack
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 bary : TEXCOORD1;
                nointerpolation float3 edges : TEXCOORD2;
                nointerpolation float3 fx : TEXCOORD3; // x the fresh lift, y the flash, z the fade
            };

            Varyings vert(Attributes v)
            {
                StructureInstance s = InstanceOf(v.instanceID);
                float3 w = PlacePiece(s, v.positionOS, v.chunk.y);
                float fresh = saturate(1 - (_RC_Clock.x - s.flashAt) / FRESH_SECONDS);
                Varyings o;
                o.positionCS = TransformWorldToHClip(w);
                o.positionWS = w;
                o.bary = v.bary.xyz;
                o.edges = v.edges;
                o.fx = float3(_Beat * fresh * fresh, FlashOf(s), s.fade);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.positionWS.y + 0.02);
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(StructureSurface(i.positionWS, i.bary, i.edges, i.fx.x, i.fx.y, 0) * i.fx.z, 1);
            }
            ENDHLSL
        }
    }
}
