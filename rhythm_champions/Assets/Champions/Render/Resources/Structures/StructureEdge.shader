// A standing structure in the edge look (design.md, The World > Structures; plan.md, Rendering > The edge look): its
// Synty model (StructureType.Body, a style mesh) drawn as the zone's dim fill under the monsters' feature lines in the
// zone's edge colour (StructureCommon.hlsl), and a line where it meets the ground. The beat wave flares the lines as it
// passes; a hit flashes the structure. Opaque, drawn from both sides (a Synty model has open sheets: leaves, planks),
// hiding the lines behind it.
Shader "Champions/StructureEdge"
{
    Properties
    {
        _Fill ("Fill gain (over the zone's fill)", Float) = 0.5
        _Edge ("Edge gain (over the zone's edge)", Float) = 3
        _EdgePx ("Edge width (px at 2160 rows, the trailer's capture)", Float) = 1.25
        _Beat ("Edge lift as the beat wave passes", Float) = 0.6
        _Flash ("A hit's lift toward the edge colour", Float) = 1.2
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

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 bary : TEXCOORD3;
                float3 edges : TEXCOORD5;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 bary : TEXCOORD1;
                nointerpolation float3 edges : TEXCOORD2;
                float2 fx : TEXCOORD3; // x the beat wave's flare, y the flash
            };

            Varyings vert(Attributes v)
            {
                StructureInstance s = InstanceOf(v.instanceID);
                float3 w = PlaceStructure(s, v.positionOS);
                Varyings o;
                o.positionCS = TransformWorldToHClip(w);
                o.positionWS = w;
                o.bary = v.bary.xyz;
                o.edges = v.edges;
                o.fx = float2(_Beat * BeatWaveAt(w.xz), FlashOf(s));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.positionWS.y);
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(StructureSurface(i.positionWS, i.bary, i.edges, i.fx.x, i.fx.y, 1), 1);
            }
            ENDHLSL
        }
    }
}
