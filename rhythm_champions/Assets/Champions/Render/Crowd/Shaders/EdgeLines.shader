// An edge cut's lines (design.md, The Look > Edge cuts; plan.md, Rendering > Crowd): each segment a quad widened in
// the vertex shader to a constant width in pixels (across it only, so polylines join without overlapping), lifted a little toward the camera so the body behind it never
// covers it, and drawn additively in the monster's tint (the palette's, through the instance) times the look's gain.
// Small on screen, the less important lines fade out first; the beat wave and the monster's own flare brighten
// them, a hit flashes them toward white. EDGE_SHARD draws the shatter's pieces' lines (EdgeCommon.hlsl), white
// on the break and fading as they sink. Under _EdgeIdMask, flat white.
Shader "Champions/EdgeLines"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "EdgeLines"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ EDGE_SHARD
            #include "EdgeCommon.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;    // this end
                float4 other : TEXCOORD0;        // the other end, and the side (-1 or 1) of this corner
                float4 boneIndex : TEXCOORD1;
                float4 boneWeight : TEXCOORD2;
                float4 otherIndex : TEXCOORD3;
                float4 otherWeight : TEXCOORD4;
                float4 seg : TEXCOORD5;          // x importance, y piece
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation half3 color : TEXCOORD0;
                float across : TEXCOORD1;        // pixels from the line's centre
                nointerpolation float halfPx : TEXCOORD2;
                float heightWS : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Owner o = OwnerOf(v.instanceID);
                float3 own = Place(o, v.positionOS, v.boneIndex, v.boneWeight, v.seg.y);
                float3 other = Place(o, v.other.xyz, v.otherIndex, v.otherWeight, v.seg.y);
                float lift = _EdgeLines.z * o.scale;
                float3 ownLifted = own + normalize(_WorldSpaceCameraPos - own) * lift;
                float3 otherLifted = other + normalize(_WorldSpaceCameraPos - other) * lift;

                float4 a = TransformWorldToHClip(ownLifted);
                float4 b = TransformWorldToHClip(otherLifted);
                float2 halfScreen = 0.5 * _ScaledScreenParams.xy;
                float2 d = b.xy / b.w * halfScreen - a.xy / a.w * halfScreen;
                float len = length(d);
                d = len > 1e-4 ? d / len : float2(1, 0);
                float halfPx = 0.5 * _EdgeLines.y * max(1.0, _ScaledScreenParams.y / 1080.0);
                // Across the line only: segments meet end to end, and a cap past each end would overlap its
                // neighbour's, doubling the light at every joint.
                float reach = halfPx + 1; // a pixel more for the antialiased edge
                a.xy += float2(-d.y, d.x) * (v.other.w * reach) / halfScreen * a.w;

                Varyings r;
                r.positionCS = a;
                r.across = v.other.w * reach;
                r.halfPx = halfPx;
                r.heightWS = own.y;
                if (_EdgeIdMask > 0.5)
                {
                    r.color = 1;
                    return r;
                }
                // The importance fade: from every line at _EdgeFade.x pixels tall to only those of importance
                // _EdgeFade.z at _EdgeFade.y.
                float cut = saturate((_EdgeFade.x - HeightPx(o)) / max(_EdgeFade.x - _EdgeFade.y, 1)) * _EdgeFade.z;
                float shown = smoothstep(cut - 0.08, cut + 0.08, v.seg.x);
                float3 lit = o.tint * _EdgeLines.x * (1 + WaveFlare(o) + o.flare);
                lit = lerp(lit, max(lit, 1.5), max(o.hitFlash, o.flash));
                r.color = lit * shown * o.fade;
                return r;
            }

            half4 frag(Varyings i) : SV_Target
            {
            #ifdef EDGE_SHARD
                clip(i.heightWS);
            #endif
                float coverage = saturate(i.halfPx + 0.5 - abs(i.across));
                if (_ChampionsIdPass > 0.5) clip(coverage - 0.5); // the ID pass marks the line, not its fringe
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(i.color * coverage, 1);
            }
            ENDHLSL
        }
    }
}
