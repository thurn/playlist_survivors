// Default airborne burst: the pinned MonsterEdgeShard drawing with per-chunk contraction before ground contact.
// The edge look's monster shards (MonsterShatter.cs): the frozen look package's MonsterEdgeShard.shader, which drew
// the neon edge trailer's shatter, ported by review. The type's chunks, one instance per death, skinned from the
// palette frozen on the break and moved rigidly with each chunk, in the bodies' surface (MonsterEdge.hlsl). Every
// chunk is outlined where it broke (the shard baker's crack edges in TEXCOORD5) as well as on the model's feature
// edges, so a monster shatters into edge-lit glass, its weapon with it. The outlines are hottest on the break's frame,
// fading over FRESH_SECONDS, and the set flashes white on the impact frame only. In the stills' type-ID pass it writes
// the effects' code. What changed from the trailer's: no shadow caster or fog; the geometry queue.
Shader "Champions/MonsterBurstShard"
{
    Properties
    {
        [HDR] _Tint ("Hue (linear, levelled)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (void.body, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 1.4
        _EdgePx ("Edge width (px at 2160 rows, the trailer's capture)", Float) = 1.25
        _Beat ("Edge lift when fresh", Float) = 0.6
        _CoreGlow ("Core at rest (unused)", Float) = 0
        _CoreFlare ("Core on the beat (unused)", Float) = 0
        _CoreSize ("Core radius (unused)", Float) = 0
        _Springs ("Motion springs (unused)", Float) = 0
        _Swarm ("Motion swarm (unused)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Core; half4 _Flash;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat;
            half _CoreGlow; half _CoreFlare; half _CoreSize;
            half _Springs; half _Swarm;
        CBUFFER_END
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterShards.hlsl"
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterEdge.hlsl"

        StructuredBuffer<Death> _Deaths;
        StructuredBuffer<Shard> _Shards;
        uint _ShardCount, _RingStart, _RingCapacity;
        float _Now;

        #define FLASH_SECONDS 0.12
        #define FRESH_SECONDS 0.6

        // The joined chunks (MonsterShardBaker): the style mesh's attributes, the chunk and the edges.
        struct ShardAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 boneIndex : TEXCOORD1;
            float4 boneWeight : TEXCOORD2;
            float4 bary : TEXCOORD3;
            float2 chunk : TEXCOORD4; // x 1 on an inside face, y the chunk's index
            float3 edges : TEXCOORD5; // the triangle's feature and crack edges
            uint instanceID : SV_InstanceID;
        };

        struct ShardVertex
        {
            float3 positionWS;
            float3 normalWS;
            float flash;
            float fresh;
            float visible;
        };

        ShardVertex ShardSkin(ShardAttributes v)
        {
            InitIndirectDrawArgs(0);
            uint slot = (_RingStart + GetIndirectInstanceID_Base(v.instanceID)) % _RingCapacity;
            Death d = _Deaths[slot];
            Shard s = _Shards[slot * _ShardCount + (uint)v.chunk.y];
            MonsterInstance m = (MonsterInstance)0;
            m.position = d.position;
            m.yaw = d.yaw;
            m.scale = d.scale;
            float3 p, n;
            SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, p, n);
            ShardVertex o;
            // Contract each separated chunk on its airborne trajectory; never fade
            // the intact living figure or leave a dark floor-covering corpse.
            float remaining = s.vanishAt - _Now;
            float shrink = saturate(remaining / 0.12);
            o.positionWS = s.position + Rotate(s.rotation, p - s.origin) * shrink;
            o.normalWS = Rotate(s.rotation, n);
            float age = _Now - d.time;
            o.flash = saturate(1 - age / FLASH_SECONDS);
            o.fresh = saturate(1 - age / FRESH_SECONDS);
            o.visible = remaining;
            return o;
        }
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
                nointerpolation float4 fx : TEXCOORD2; // x flash, y inside, z fresh
                float4 bary : TEXCOORD5;
                nointerpolation float3 edges : TEXCOORD6;
            };

            Varyings vert(ShardAttributes v)
            {
                ShardVertex c = ShardSkin(v);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.flash, v.chunk.x, c.fresh * c.fresh, c.visible);
                o.bary = v.bary;
                o.edges = v.edges;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(i.fx.w);
                CHAMPIONS_ID_PASS(_ChampionsId)
                float lit, fres;
                // Fresh outlines are lifted as the bodies' are on the beat: the break pays off on its frame.
                float3 color = EdgeSurface(i.positionWS, i.normalWS, i.bary, i.edges, _Tint.rgb, 2.0 * i.fx.z, i.fx.y, _Rim, lit, fres);
                color += _Flash.rgb * i.fx.x * LookFlash(LOOK_SHARD_CALM) * (2.0 * lit + fres + 0.35);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
