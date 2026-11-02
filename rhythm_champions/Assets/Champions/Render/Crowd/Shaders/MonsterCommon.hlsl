// The monsters' vertex front end (MonsterEdge.shader and MonsterEdgeShard.shader include it), ported by review from
// the frozen look package's MonsterCommon.hlsl: the style mesh's attributes, the palette skinning, the instance's hit
// flash, tint and LOD fade; and the world's hooks, which stand in for the trailer's beat globals and impulse rings
// (World.hlsl): the beat wave's flare is a monster's pulse (the trailer's _SL_Pulse, now travelling out from the hero
// with the wave rather than lighting every monster at once), the wave's bob plus the event rings' hop lift it, and the
// event rings light its rim and lines. The shader declares its UnityPerMaterial CBUFFER (with `half4 _Tint;`,
// `half _Springs; half _Swarm;`) before including this.
#ifndef CHAMPIONS_MONSTER_COMMON_INCLUDED
#define CHAMPIONS_MONSTER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Champions/Render/Shaders/IdPass.hlsl"
#include "Assets/Champions/Render/World/Shaders/World.hlsl"
#include "CrowdCommon.hlsl"

// The monsters' hit flash and their shards' break flash outside special moments (World.hlsl's LookFlash).
#define LOOK_HIT_CALM 0.35
#define LOOK_SHARD_CALM 0.5

// The draw's code in the stills' type-ID pass (IdPass): its type's (MonsterCrowd.SetIdCode), or the shards' effect code.
float _ChampionsId;

// The crowd's style mesh (StyleMesh.Unweld): every triangle its own vertices, the rest pose in root space at roster
// height, bone data in TEXCOORD1/2, barycentrics plus a random value per triangle in TEXCOORD3 and the triangle's
// feature edges in TEXCOORD5 (component k 1 where the edge opposite corner k is a real edge of the model).
struct MonsterAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 boneIndex : TEXCOORD1;
    float4 boneWeight : TEXCOORD2;
    float4 bary : TEXCOORD3;
    float3 edges : TEXCOORD5;
    uint instanceID : SV_InstanceID;
};

struct MonsterVertex
{
    float3 positionWS;
    float3 normalWS;
    float4 fx;    // x hit, y the instance's own flare, zw the LOD fade (CrowdLodFade)
    float3 tint;  // linear
};

MonsterVertex MonsterSkin(MonsterAttributes v)
{
    MonsterVertex o;
    uint slot = CrowdSlot(v.instanceID);
    MonsterInstance m = _Instances[slot];
    SkinVertex(m, slot, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
    o.fx = float4(m.hitFlash, (m.flags & CROWD_ALIVE) != 0 ? m.flare : 0, CrowdLodFade(slot));
    o.tint = m.tint != 0 ? TintOf(m.tint) : _Tint.rgb;
    return o;
}

// The world at a monster standing at rootXZ (the trailer's ImpulseCrowd, on the world's beat wave and event rings): its
// hop (m, up), the event rings' light on it (linear) and its pulse (0 to 1: the beat wave's flare passing it).
void MonsterWorld(float2 rootXZ, out float hop, out float3 light, out float pulse)
{
    float bob, strength;
    float2 push;
    BeatWaveCrowd(rootXZ, pulse, bob);
    ImpulseAt(rootXZ, strength, light, push);
    hop = bob + 0.12 * saturate(strength);
}

#endif
