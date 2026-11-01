// The wireframe hologram monster look's shards (HologramLook.cs; MonsterShatter.cs): the type's
// chunks, one instance per death, posed and moved as MonsterShard.shader poses them (MonsterShards.hlsl),
// in the bodies' surface (MonsterHologram.hlsl) with little fill: line fragments. Every chunk is
// outlined where it broke (the shard baker's crack edges in TEXCOORD5) as well as on the model's
// feature edges (or every edge, under wireframe-all). The lines are hottest on the break's frame,
// fading over FRESH_SECONDS, and the set flashes shatter.core on the impact frame only, as the crowd's
// hit flash fades. Depth first, then light added, as the bodies draw. Under the global _GameIdMask
// (the stills' ID render) it draws flat white.
Shader "Look/MonsterHologramShard"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.1, 0.8, 1.0, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _AllEdges ("Every triangle edge a line", Float) = 0
        _Fill ("Fill face-on", Float) = 0.02
        _FillRim ("Fill toward the silhouette", Float) = 0.06
        _Veil ("Fill opacity: how much it dims what is behind", Float) = 0.1
        _Rim ("Rim gain", Float) = 0.6
        _RimPower ("Rim falloff", Float) = 5.0
        _Edge ("Line gain", Float) = 1.8
        _EdgePx ("Line width (px)", Float) = 1.1
        _Beat ("Line lift when fresh", Float) = 0.6
        _Sweep ("Beat band gain (unused)", Float) = 0
        _SweepWidth ("Beat band half-width (unused)", Float) = 0.08
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
            half4 _Tint; half4 _Flash;
            half _AllEdges; half _Fill; half _FillRim; half _Veil; half _Rim; half _RimPower;
            half _Edge; half _EdgePx; half _Beat; half _Sweep; half _SweepWidth;
            half _Gain; half _Peak; half _Facets; // MonsterCommon.hlsl's, unused here
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterShards.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterHologram.hlsl"

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

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            nointerpolation float4 fx : TEXCOORD2; // x flash, y inside, z fresh
            float fogFactor : TEXCOORD3;
            float4 bary : TEXCOORD4;
            nointerpolation float3 edges : TEXCOORD5;
        };

        Varyings vert(ShardAttributes v)
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
            float3 positionWS = s.position + Rotate(s.rotation, p - s.origin);
            float age = _Now - d.time;
            float fresh = saturate(1 - age / FRESH_SECONDS);
            Varyings o;
            o.positionWS = positionWS;
            o.positionCS = TransformWorldToHClip(positionWS);
            o.normalWS = Rotate(s.rotation, n);
            o.fx = float4(saturate(1 - age / FLASH_SECONDS), v.chunk.x, fresh * fresh, 0);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            o.bary = v.bary;
            o.edges = v.edges;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "HologramDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float lit, fres;
                // Fresh lines are lifted as the bodies' are on the beat: the break pays off on its frame.
                float3 color = HologramSurface(i.positionWS, i.normalWS, i.bary, i.edges, _Tint.rgb, 2.0 * i.fx.z, i.fx.y, lit, fres);
                color += _Flash.rgb * i.fx.x * (1.2 * lit + fres + 0.2);
                // The fill is a faint veil over what is behind (premultiplied: its light added, the world dimmed
                // by _Veil), so a near monster's body sets it off from the one behind it. Both fade into the
                // fog, which takes neither's colour.
                return half4(MixFogColor(color, 0, i.fogFactor), MixFogColor(_Veil.xxx, 0, i.fogFactor).x);
            }
            ENDHLSL
        }
    }
}
