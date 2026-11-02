// The solid-emissive monster look's shards (SolidLook.cs; MonsterShatter.cs): the type's chunks, one
// instance per death, posed and moved as MonsterShard.shader poses them (MonsterShards.hlsl), self-lit
// in the bodies' hue and surface with the bodies' dark outline (MonsterSolid.hlsl), so a solid monster
// breaks into solid, outlined chunks; fresh inside faces are a little brighter. The set flashes
// shatter.core on the impact frame, nearly gone two frames later. Under the global
// _GameIdMask (the stills' ID render) the chunks draw flat white and the outline nothing.
Shader "Look/MonsterSolidShard"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.6, 0.2, 1.0, 1)
        _Outline ("Outline (void.floor, linear)", Color) = (0.002, 0.001, 0.003, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Glow ("Emission gain", Float) = 1.0
        _Key ("Light from above", Range(0, 1)) = 0.55
        _Falloff ("Silhouette band darkness", Range(0, 1)) = 0.9
        _FalloffPx ("Silhouette band (px at 1080 rows)", Float) = 3
        _Facets ("Faceting", Range(0, 1)) = 0.5
        _HotSpot ("Hot spot on the beat (unused)", Float) = 0
        _Impulse ("Impulse light (unused)", Float) = 0
        _OutlinePx ("Outline width (px at 1080 rows)", Float) = 3.5
        _Gain ("Horde gain (unused)", Float) = 1
        _Peak ("Horde peak (unused)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Outline; half4 _Flash;
            half _Glow; half _Key; half _Falloff; half _FalloffPx; half _Facets;
            half _HotSpot; half _Impulse; half _OutlinePx;
            half _Gain; half _Peak;
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterShards.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterSolid.hlsl"

        StructuredBuffer<Death> _Deaths;
        StructuredBuffer<Shard> _Shards;
        uint _ShardCount, _RingStart, _RingCapacity;
        float _Now;

        #define FLASH_SECONDS 0.12 // MonsterTrack.FlashSeconds

        // The joined chunks (CrowdShardBaker): the style mesh's attributes, and the chunk.
        struct ShardAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 boneIndex : TEXCOORD1;
            float4 boneWeight : TEXCOORD2;
            float4 bary : TEXCOORD3;
            float2 chunk : TEXCOORD4; // x 1 on an inside face, y the chunk's index
            uint instanceID : SV_InstanceID;
        };

        struct ShardVertex
        {
            float3 positionWS;
            float3 normalWS;
            float3 pivot; // the chunk's (world), the outline's centre
            float flash;
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
            o.pivot = s.position;
            // White on the impact frame, nearly gone two frames on: a mass kill's flash is one frame,
            // not a pale blob fading over the crowd's whole flash.
            float flash = saturate(1 - (_Now - d.time) / FLASH_SECONDS);
            o.flash = flash * flash * flash;
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
                nointerpolation float2 fx : TEXCOORD2; // x flash, y inside
                float fogFactor : TEXCOORD3;
                float4 bary : TEXCOORD4;
            };

            Varyings vert(ShardAttributes v)
            {
                ShardVertex c = ShardSkin(v);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float2(c.flash, v.chunk.x);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float3 color = SolidSurface(i.positionWS, i.normalWS, i.bary, _Tint.rgb, 0, i.fx.y, 12); // a chunk is a few pixels across
                color = lerp(color, _Flash.rgb, i.fx.x * LookFlash(LOOK_SHARD_CALM));
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float fogFactor : TEXCOORD0;
            };

            Varyings vert(ShardAttributes v)
            {
                ShardVertex c = ShardSkin(v);
                Varyings o;
                o.positionCS = MonsterOutlineCS(c.positionWS, c.normalWS, c.pivot, _OutlinePx);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(0.5 - _GameIdMask);
                return half4(MixFog(_Outline.rgb, i.fogFactor), 1);
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
