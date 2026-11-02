// The edge look's monster bodies (design.md, The Look; plan.md, Rendering > The edge look): MonsterEdge.shader as the
// frozen look package had it when the neon edge trailer was captured (git 138684f2; the outline hull and the small
// body's brighter rim came after), ported by review. The crowd's style mesh skinned from the palette
// (MonsterCommon.hlsl), drawn as near-black glass lit only by the type's hue in its rim, its feature edges and its inner
// core (MonsterEdge.hlsl). As the world's beat wave passes, a living monster flares and bobs; the event rings light its
// rim and lines and hop it. What changed from the trailer's: the world's globals stand in for its beat globals and
// impulse rings (MonsterWorld), the stills' type-ID pass for its mask, and the shadow caster, fog and shadow proxy are
// gone (the game draws no shadows or fog); opaque in the geometry queue, as the game's other opaque drawers. The
// trailer's post is the game's too (Grade.cs). --look trailer draws with exactly this shader and EdgeLook.Trailer's gains (EdgeLookTests pins
// both); a change to the default look gets its own shader.
Shader "Champions/MonsterEdge"
{
    Properties
    {
        [HDR] _Tint ("Hue (linear, levelled)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (void.body, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 2.6
        _EdgePx ("Edge width (px at 2160 rows, the trailer's capture)", Float) = 1.25
        _Beat ("Edge lift on the beat", Float) = 0.3
        _Ring ("Ring light on rim and lines", Float) = 1.1
        _CoreGlow ("Core at rest", Float) = 0.1
        _CoreFlare ("Core on the beat", Float) = 0.35
        _CoreSize ("Core radius (share of the body's)", Float) = 0.18
        _Springs ("Motion springs (0 or 1)", Float) = 1
        _Swarm ("Motion swarm (0 or 1)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Core; half4 _Flash;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat; half _Ring;
            half _CoreGlow; half _CoreFlare; half _CoreSize;
            half _Springs; half _Swarm;
        CBUFFER_END
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterEdge.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y the pulse (living only), zw the LOD fade
                nointerpolation float3 tint : TEXCOORD3;
                float4 bary : TEXCOORD5;
                nointerpolation float3 edges : TEXCOORD6;
                nointerpolation float4 core : TEXCOORD7; // the body's bounding sphere (CrowdSphere): centre (world), radius
                nointerpolation float3 ring : TEXCOORD8; // the event rings' light where it stands (linear)
            };

            Varyings vert(MonsterAttributes v)
            {
                MonsterVertex c = MonsterSkin(v);
                MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
                float height;
                Varyings o;
                float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
                float hop, pulse;
                MonsterWorld(m.position.xz, hop, o.ring, pulse);
                o.ring *= alive;
                c.positionWS.y += hop * alive;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.fx.x, alive * max(pulse, c.fx.y), c.fx.zw);
                o.tint = _Tint.rgb; // the type's hue, not the instance's
                o.bary = v.bary;
                o.edges = v.edges;
                o.core = CrowdSphere(m, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                CHAMPIONS_ID_PASS(_ChampionsId)
                float lit, fres;
                float3 color = EdgeSurface(i.positionWS, i.normalWS, i.bary, i.edges, i.tint, i.fx.y, 0, _Rim, lit, fres);
                float3 core = EdgeCore(i.positionWS, i.core, i.tint, i.fx.y);
                color += core;
                color += i.ring * _Ring * (lit + fres);
                // A hit lights what is lit already toward white: the lines, the rim and the core.
                color += _Flash.rgb * i.fx.x * LookFlash(LOOK_HIT_CALM) * (2.0 * lit + fres + 0.5 * Luminance(core));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
