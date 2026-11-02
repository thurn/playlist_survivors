// The GPU shatter's records (EdgeShatter.cs; game/'s MonsterShards.hlsl, ported), shared by its compute passes
// (EdgeShards.compute) and the edge shaders' shard variant (EdgeCommon.hlsl).
#ifndef CHAMPIONS_EDGE_SHARDS_INCLUDED
#define CHAMPIONS_EDGE_SHARDS_INCLUDED

// Matches EdgeShatter.Death (64 bytes).
struct Death
{
    float3 position; float yaw;   // the body's placement on its break (its MonsterInstance's)
    float3 hit; float scale;      // where the blow landed (world)
    float2 blow; float force; float time; // the blow on the ground (unit or zero), its launch distance (m), the break's song time
    uint tint; uint seed; uint crowdSlot; uint slot; // line colour; per-death hash seed; palette slot; ring slot
};

// Matches EdgeShatter.ShardStride (80 bytes): one piece of one death.
struct Shard
{
    float3 position; float time;  // its pivot (world); the song time it is integrated to
    float4 rotation;              // its turn since the break (a quaternion)
    float3 velocity; float sinkFrom; // m/s; its height when the sink began, or -1
    float3 spin; float radius;    // rad/s (world); its contact radius on the ground (m)
    float3 origin; float pad;     // its pivot on the break (world)
};

// Matches EdgeShardPiece (48 bytes).
struct PieceRest
{
    float3 pivot; float size;
    float4 bones; float4 weights;
};

// v turned by the unit quaternion q.
float3 Rotate(float4 q, float3 v)
{
    float3 t = 2 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

#endif
