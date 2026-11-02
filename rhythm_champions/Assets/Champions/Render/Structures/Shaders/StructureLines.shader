// A structure's edges (design.md, The World > Structures, The Look > The beat wave): each segment a quad widened in the
// vertex shader to a constant width in pixels (across it only, as the crowd's lines), lifted a little toward the camera
// so the fill never covers it, drawn additively in the zone's edge colour where it stands. The beat wave flares them
// as it passes; a hit or a break flashes them.
Shader "Champions/StructureLines"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "StructureLines"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "StructureCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;   // this end
                float4 other : TEXCOORD0;       // the other end, and the side (-1 or 1) of this corner
                float4 seg : TEXCOORD1;         // x importance, y piece
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float across : TEXCOORD1;       // pixels from the line's centre
                nointerpolation float halfPx : TEXCOORD2;
                nointerpolation float gain : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                StructureInstance s = InstanceOf(v.instanceID);
                float3 own = PlaceStructure(s, v.positionOS, v.seg.y);
                float3 other = PlaceStructure(s, v.other.xyz, v.seg.y);
                float lift = _StructureLines.y;
                float4 a = TransformWorldToHClip(own + normalize(_WorldSpaceCameraPos - own) * lift);
                float4 b = TransformWorldToHClip(other + normalize(_WorldSpaceCameraPos - other) * lift);
                float2 halfScreen = 0.5 * _ScaledScreenParams.xy;
                float2 d = b.xy / b.w * halfScreen - a.xy / a.w * halfScreen;
                float len = length(d);
                d = len > 1e-4 ? d / len : float2(1, 0);
                float halfPx = 0.5 * _StructureLines.x * max(1.0, _ScaledScreenParams.y / 1080.0);
                float reach = halfPx + 1; // a pixel more for the antialiased edge
                a.xy += float2(-d.y, d.x) * (v.other.w * reach) / halfScreen * a.w;

                Varyings r;
                r.positionCS = a;
                r.positionWS = own;
                r.across = v.other.w * reach;
                r.halfPx = halfPx;
                float flare = _StructureLook.z * BeatWaveAt(own.xz);
                r.gain = _StructureLook.y * (1 + flare) * (1 + FlashOf(s) * _StructureLook.w) * s.fade;
                return r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.positionWS.y + 0.02);
                float coverage = saturate(i.halfPx + 0.5 - abs(i.across));
                if (_ChampionsIdPass > 0.5) clip(coverage - 0.5); // the ID pass marks the line, not its fringe
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(ZoneColour(i.positionWS.xz, ZONE_EDGE) * (i.gain * coverage), 1);
            }
            ENDHLSL
        }
    }
}
