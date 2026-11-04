// A Boss Rush arena in the edge look (boss-rush.md, Arenas; design.md, The aim: retro 3D neon wireframe): its walls,
// raised floor, props, pits and floor zones as low-poly facets, a dark body under pixel-wide lines on the facets' edges
// (MonsterLines.hlsl's EdgeLine, as the structures draw) in the line colour: the reserved wall grey, the zone key's edge
// colour for a breakable prop (_ZoneLine), the warning colour for a hazard. The beat wave flares the lines as it passes.
// Forming draws it in: geometry past _Reveal along its outline (TEXCOORD0.x) is clipped and its height scales by _Rise.
// Opaque by default; a zone's floor cap draws translucent (_SrcBlend/_DstBlend/_ZWrite, premultiplied), so the grid
// shows through it. A float-driven shader: no keywords, no variants beyond its one pass.
Shader "Champions/ArenaEdge"
{
    Properties
    {
        [HDR] _Line ("Line colour (linear, HDR)", Color) = (0.27, 0.27, 0.27, 1)
        _Fill ("Body colour (linear)", Color) = (0.0025, 0.0028, 0.004, 1)
        _Alpha ("Body opacity (premultiplied; 1 opaque)", Float) = 1
        _Edge ("Line gain", Float) = 2.6
        _EdgePx ("Line width (px at 2160 rows, the trailer's capture)", Float) = 1.25
        _Beat ("Line lift as the beat wave passes", Float) = 0.5
        _ZoneLine ("Lines in the zone key's edge colour (0 or 1)", Float) = 0
        _Reveal ("Drawn along its outline so far (0 to 1)", Float) = 1
        _Rise ("Height drawn so far (0 to 1)", Float) = 1
        _ChampionsId ("Type-ID pass code", Float) = 251
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 0
        _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Line; half4 _Fill; half _Alpha; half _Edge; half _EdgePx; half _Beat; half _ZoneLine;
                float _Reveal; float _Rise; float _ChampionsId; float _SrcBlend; float _DstBlend; float _ZWrite;
            CBUFFER_END
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            #include "Assets/Champions/Render/World/Shaders/World.hlsl"
            #include "Assets/Champions/Render/Crowd/Shaders/MonsterLines.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 order : TEXCOORD0; // x its place along the outline (0 to 1), y unused
                float4 bary : TEXCOORD3;
                float3 edges : TEXCOORD5;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 bary : TEXCOORD1;
                nointerpolation float3 edges : TEXCOORD2;
                float order : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                float3 p = v.positionOS;
                p.y *= _Rise;
                Varyings o;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.bary = v.bary.xyz;
                o.edges = v.edges;
                o.order = v.order.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(_Reveal - i.order + 1e-4);
                CHAMPIONS_ID_PASS(_ChampionsId)
                float lit = EdgeLine(i.bary, i.edges, _EdgePx);
                float3 lineColour = _ZoneLine > 0.5 ? ZoneColour(i.positionWS.xz, ZONE_EDGE) : _Line.rgb;
                float3 c = Ungraded(_Fill.rgb) * _Alpha + lineColour * (_Edge * lit * (1 + _Beat * BeatWaveAt(i.positionWS.xz)));
                return half4(c, saturate(_Alpha + lit));
            }
            ENDHLSL
        }
    }
}
