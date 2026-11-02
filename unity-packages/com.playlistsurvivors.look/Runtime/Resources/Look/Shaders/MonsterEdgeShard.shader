// The dark-core-edges monster look's shards (EdgeLook.cs; MonsterShatter.cs): the type's chunks,
// one instance per death, posed and moved as every look's shards are (MonsterShards.hlsl), in
// the bodies' surface (MonsterEdge.hlsl). Every chunk is outlined where it broke (the shard baker's
// crack edges in TEXCOORD5) as well as on the model's feature edges, so an edge monster shatters
// into edge-lit glass. The outlines are hottest on the break's frame, fading over FRESH_SECONDS,
// and the set flashes shatter.core on the impact frame only, as the crowd's hit flash fades. Under
// the global _GameIdMask (the stills' ID render) it draws flat white.
Shader "Look/MonsterEdgeShard"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (void.floor, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 1.4
        _EdgePx ("Edge width (px)", Float) = 1.25
        _Beat ("Edge lift when fresh", Float) = 0.6
        _CoreGlow ("Core at rest (unused)", Float) = 0
        _CoreFlare ("Core on the beat (unused)", Float) = 0
        _CoreSize ("Core radius (unused)", Float) = 0
        _Gain ("Horde gain (unused)", Float) = 1
        _Peak ("Horde peak (unused)", Float) = 1
        _Facets ("Faceting (unused)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Core; half4 _Flash;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat;
            half _CoreGlow; half _CoreFlare; half _CoreSize;
            half _Gain; half _Peak; half _Facets;
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterShards.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterEdge.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"

        StructuredBuffer<Death> _Deaths;
        StructuredBuffer<Shard> _Shards;
        uint _ShardCount, _RingStart, _RingCapacity;
        float _Now;

        #define FLASH_SECONDS 0.12 // MonsterTrack.FlashSeconds
        #define FRESH_SECONDS 0.6

        // The joined chunks (CrowdShardBaker): the style mesh's attributes, the chunk and the edges.
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
            o.positionWS = s.position + Rotate(s.rotation, p - s.origin);
            o.normalWS = Rotate(s.rotation, n);
            float age = _Now - d.time;
            o.flash = saturate(1 - age / FLASH_SECONDS);
            o.fresh = saturate(1 - age / FRESH_SECONDS);
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x flash, y inside, z fresh
                float fogFactor : TEXCOORD4;
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
                o.fx = float4(c.flash, v.chunk.x, c.fresh * c.fresh, 0);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                o.edges = v.edges;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float lit, fres;
                // Fresh outlines are lifted as the bodies' are on the beat: the break pays off on its frame.
                float3 color = EdgeSurface(i.positionWS, i.normalWS, i.bary, i.edges, _Tint.rgb, 2.0 * i.fx.z, i.fx.y, _Rim, lit, fres);
                color += _Flash.rgb * i.fx.x * LookFlash(LOOK_SHARD_CALM) * (2.0 * lit + fres + 0.35);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(ShardAttributes v) : SV_POSITION
            {
                ShardVertex c = ShardSkin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
