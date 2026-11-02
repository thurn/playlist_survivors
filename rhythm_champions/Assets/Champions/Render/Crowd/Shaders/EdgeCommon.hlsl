// The edge cuts' draw-side code (design.md, The Look > Edge cuts; plan.md, Rendering > Crowd), shared by EdgeBody
// and EdgeLines: who owns a vertex and how it is skinned and placed. Two variants:
// - the crowd (default): an instance from the culling pass's visible list, skinned from the crowd's palette
//   (CrowdPalette.compute: 4 matrices per vertex), placed by its scale, yaw and position;
// - EDGE_SHARD: one death of the shatter's ring, skinned from the palette it froze on the break, then each vertex
//   moved rigidly with its piece (EdgeShards.compute), clipped under the floor and fading as the piece sinks.
// The look's inputs are globals (EdgeLook.cs sets them): the lines' gain and width, the body's colour, the
// importance fade's sizes, the bench's beat wave and the stills' mask mode; the world's beat wave (World.hlsl, the
// world's globals) flares a monster's lines and bobs it as a ring passes it.
#ifndef CHAMPIONS_EDGE_COMMON_INCLUDED
#define CHAMPIONS_EDGE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Champions/Render/Shaders/IdPass.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "CrowdSkinning.hlsl"
#include "EdgeShards.hlsl"
#include "Assets/Champions/Render/World/Shaders/World.hlsl"

StructuredBuffer<float4> _Palette;  // 3 rows per bone, by crowd slot (or, for shards, by ring slot)
uint _BoneCount;
float _HeightM;                     // the type's standing height at scale 1
#ifdef EDGE_SHARD
StructuredBuffer<Death> _Deaths;
StructuredBuffer<Shard> _Shards;
uint _PieceCount, _RingStart, _RingCapacity;
float _Now;
#define SHARD_FLASH_SECONDS 0.1     // the break's white flash
#define SHARD_LINGER 1.2            // EdgeShatter.LingerSeconds
#define SHARD_SINK 0.6              // EdgeShatter.SinkSeconds
#else
StructuredBuffer<MonsterInstance> _Instances;
StructuredBuffer<uint> _VisibleList;
#endif

float4 _EdgeLines;     // x the lines' gain, y their width in pixels at 1080 rows, z their lift toward the camera (m at scale 1)
float4 _EdgeBody;      // rgb the body's colour (linear), a a hit's lift of it toward the tint
float4 _EdgeFade;      // x the height in pixels from which every line shows, y the height at which only the most
                       // important show, z the importance a line then needs
float4 _EdgeWave;      // xy the beat wave's centre on the ground (world x, z), z its radius (m), w its strength
float _EdgeWaveWidth;  // the wave's half width (m)
float _EdgeIdMask;     // 1 while the stills draw their mask: every edge cut flat white
float _ChampionsId;    // the draw's code in the stills' type-ID pass (IdPass): its type's, or the shards' effect code

struct Owner
{
    uint slot;            // the palette's slot
    float3 position;
    float yaw, scale;
    float3 tint;          // linear
    float hitFlash, flare;
    float fade, flash;    // a shard's fade as it sinks and its break flash (1 and 0 for the crowd)
#ifdef EDGE_SHARD
    uint shardBase;       // its pieces' first record
#endif
};

float3 TintOf(uint tint)
{
    return SRGBToLinear(float3(tint & 0xFF, (tint >> 8) & 0xFF, (tint >> 16) & 0xFF) / 255.0);
}

Owner OwnerOf(uint instanceID)
{
    InitIndirectDrawArgs(0);
    uint i = GetIndirectInstanceID_Base(instanceID);
    Owner o;
#ifdef EDGE_SHARD
    uint slot = (_RingStart + i) % _RingCapacity;
    Death d = _Deaths[slot];
    o.slot = slot;
    o.position = d.position;
    o.yaw = d.yaw;
    o.scale = d.scale;
    o.tint = TintOf(d.tint);
    o.hitFlash = 0;
    o.flare = 0;
    float age = _Now - d.time;
    o.flash = saturate(1 - age / SHARD_FLASH_SECONDS);
    o.fade = 1 - saturate((age - SHARD_LINGER) / SHARD_SINK);
    o.shardBase = slot * _PieceCount;
#else
    uint slot = _VisibleList[i];
    MonsterInstance m = _Instances[slot];
    o.slot = slot;
    o.position = m.position;
    o.yaw = m.yaw;
    o.scale = m.scale;
    o.tint = TintOf(m.tint);
    o.hitFlash = m.hitFlash;
    o.flare = m.flare;
    o.flash = 0;
    o.fade = 1;
    // The world's beat wave bobs it as a ring passes (its flare is WaveFlare's).
    float waveFlare, bob;
    BeatWaveCrowd(m.position.xz, waveFlare, bob);
    o.position.y += bob;
#endif
    return o;
}

float3x4 BoneMatrix(uint slot, uint bone)
{
    uint at = (slot * _BoneCount + bone) * 3;
    return float3x4(_Palette[at], _Palette[at + 1], _Palette[at + 2]);
}

// A root-space point skinned with 4 palette bones and placed in the world (for a shard, moved with its piece).
float3 Place(Owner o, float3 positionOS, float4 boneIndex, float4 boneWeight, float piece)
{
    float3x4 skin = BoneMatrix(o.slot, (uint)boneIndex.x) * boneWeight.x;
    UNITY_BRANCH if (boneWeight.y > 0) skin += BoneMatrix(o.slot, (uint)boneIndex.y) * boneWeight.y;
    UNITY_BRANCH if (boneWeight.z > 0) skin += BoneMatrix(o.slot, (uint)boneIndex.z) * boneWeight.z;
    UNITY_BRANCH if (boneWeight.w > 0) skin += BoneMatrix(o.slot, (uint)boneIndex.w) * boneWeight.w;
    float3 p = mul(skin, float4(positionOS, 1)) * o.scale;
    float sy, cy;
    sincos(o.yaw, sy, cy);
    float3 w = o.position + float3(p.x * cy + p.z * sy, p.y, -p.x * sy + p.z * cy);
#ifdef EDGE_SHARD
    Shard s = _Shards[o.shardBase + (uint)piece];
    w = s.position + Rotate(s.rotation, w - s.origin);
#endif
    return w;
}

// The owner's standing height on screen, in pixels at 1080 rows (a supersampled frame's halved).
float HeightPx(Owner o)
{
    float3 centre = o.position + float3(0, 0.5 * _HeightM * o.scale, 0);
    float depth = max(-TransformWorldToView(centre).z, 1e-3);
    float rows = _ScaledScreenParams.y;
    // abs: the projection flips y (a negative _m11) when the camera renders into a texture.
    return _HeightM * o.scale * abs(UNITY_MATRIX_P._m11) * 0.5 * rows / depth / max(1.0, rows / 1080.0);
}

// The beat wave's flare at the owner: the world's (World.hlsl) and the bench's ring of `_EdgeWaveWidth` round its
// centre at its radius.
float WaveFlare(Owner o)
{
    float d = length(o.position.xz - _EdgeWave.xy) - _EdgeWave.z;
    return _EdgeWave.w * exp(-(d * d) / max(_EdgeWaveWidth * _EdgeWaveWidth, 1e-4)) + BeatWaveAt(o.position.xz);
}

#endif
