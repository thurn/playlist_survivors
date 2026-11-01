// The refractive-gem monster look's shards (GemLook.cs; MonsterShatter.cs): the type's chunks, one
// instance per death, posed and moved as every look's shards are (MonsterShards.hlsl), in the bodies'
// gem (MonsterGem.hlsl). The fresh inside faces hold the slot's light, as a broken gem's do, and every
// chunk's crack (the shard baker's edges in TEXCOORD5) catches a pale line, both hottest on the
// break's frame and fading over FRESH_SECONDS; the set flashes shatter.core on the impact frame only,
// as the crowd's hit flash fades. Under the global _GameIdMask (the stills' ID render) it draws flat
// white.
Shader "Look/MonsterGemShard"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.3, 0.6, 1.0, 1)
        [HDR] _FringeA ("Fringe toward one neighbour (linear)", Color) = (0.2, 0.7, 0.8, 1)
        [HDR] _FringeB ("Fringe toward the other (linear)", Color) = (0.5, 0.4, 1.0, 1)
        _Core ("Gem's own dark (void.floor, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Refraction ("Refraction", Float) = 0.05
        _Behind ("Scene behind's gain", Float) = 1.6
        _Shine ("Reflected bands' gain", Float) = 1.1
        _ShineWidth ("Share of facets in a band", Range(0.05, 0.9)) = 0.2
        _Sparkle ("Trapped light when fresh", Float) = 1.2
        _Rim ("Rim gain", Float) = 1.3
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 0.7
        _EdgePx ("Line width (px)", Float) = 1.0
        _Beat ("Line lift when fresh", Float) = 0.8
        _Inside ("Fresh inside faces' light", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _FringeA; half4 _FringeB; half4 _Core; half4 _Flash;
            float _Refraction; half _Behind; half _Shine; half _ShineWidth; half _Sparkle; half _Far; // _Far unused on shards
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat; half _Inside;
            half _CoreGlow; half _CoreFlare; half _CoreSize; // MonsterEdge.hlsl's core, unused on shards
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterShards.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterEdge.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterGem.hlsl"

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
                float lit, shine;
                // Fresh shards sparkle and their lines lift as the bodies' do on the beat: the break pays off on its frame.
                float3 color = GemSurface(i.positionWS, i.normalWS, i.positionCS, i.bary, i.edges, _Tint.rgb, i.fx.z, i.fx.y, 0, lit, shine);
                color += _Tint.rgb * _Inside * i.fx.y * (0.15 + i.fx.z);
                color += GemFlash(_Tint.rgb) * i.fx.x * (1.5 * lit + shine + 0.35);
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
