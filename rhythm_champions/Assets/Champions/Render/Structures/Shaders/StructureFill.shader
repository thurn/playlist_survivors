// A structure's fill (design.md, The World > Structures): its low-poly body in the zone's dim fill colour where it
// stands, shaded a little by its faces' slope so it reads as a solid shape, drawn from both sides; a flash lifts it
// toward the zone's edge colour. Opaque: it hides what stands behind it.
Shader "Champions/StructureFill"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "StructureFill"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "StructureCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 piece : TEXCOORD1;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float shade : TEXCOORD1;
                nointerpolation float flash : TEXCOORD2;
                nointerpolation float fade : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                StructureInstance s = InstanceOf(v.instanceID);
                float3 w = PlaceStructure(s, v.positionOS, v.piece.x);
                float3 n = normalize(PlaceStructure(s, v.positionOS + v.normalOS, v.piece.x) - w);
                Varyings r;
                r.positionCS = TransformWorldToHClip(w);
                r.positionWS = w;
                // Roofs a little lighter than walls, undersides darker: one face, one shade.
                r.shade = lerp(0.62, 1.0, saturate(0.5 + 0.5 * n.y));
                r.flash = FlashOf(s);
                r.fade = s.fade;
                return r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.positionWS.y);
                CHAMPIONS_ID_PASS(_ChampionsId)
                float3 fill = ZoneColour(i.positionWS.xz, ZONE_FILL) * (_StructureLook.x * i.shade);
                float3 edge = ZoneColour(i.positionWS.xz, ZONE_EDGE);
                return half4(lerp(fill, edge, saturate(i.flash * _StructureLook.w)) * i.fade, 1);
            }
            ENDHLSL
        }
    }
}
