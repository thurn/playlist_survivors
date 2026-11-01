// The GPU shatter's shards (unity-game.md, Shatter; MonsterShatter.cs): MonsterCrystal's surface on
// the type's chunks, one instance per death. Each corner is skinned as the crowd skins it, from the
// palette frozen on the break, so the chunks start exactly where the body was; then it moves
// rigidly with its chunk's state (MonsterShards.compute), turning about the chunk's pivot. Inside
// faces are brighter and more saturated, as a broken gem's fresh faces are, and the whole set
// flashes white on the break, fading as the crowd's hit flash does. Under the global _GameIdMask
// (the stills' ID render) it draws flat white, as the crowd does.
Shader "Game/MonsterShard"
{
    Properties
    {
        [HDR] _Tint ("Gem (a death's tint replaces it)", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Horde gain", Float) = 1
        _Peak ("Horde peak", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Gain; half _Peak;
        CBUFFER_END
        #include "MonsterCommon.hlsl"
        #include "MonsterShards.hlsl"

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
            float3 tint;
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
            o.tint = d.tint != 0 ? TintOf(d.tint) : _Tint.rgb;
            o.flash = saturate(1 - (_Now - d.time) / FLASH_SECONDS);
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "../../Arena/Shaders/CrystalSurface.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x flash, y inside
                nointerpolation float3 tint : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float4 bary : TEXCOORD5;
            };

            Varyings vert(ShardAttributes v)
            {
                ShardVertex c = ShardSkin(v);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.flash, v.chunk.x, 0, 0);
                o.tint = c.tint;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float3 color = CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, i.tint, i.fx.x, 0, i.fx.y, 1, false);
                return half4(MixFog(SL_GradeOf(color * _Gain, _Peak), i.fogFactor), 1);
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
