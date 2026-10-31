// The GPU shatter's records (MonsterShatter.cs), shared by its compute passes (MonsterShards.compute)
// and its draw (MonsterShard.shader).
#ifndef GAME_MONSTER_SHARDS_INCLUDED
#define GAME_MONSTER_SHARDS_INCLUDED

// Matches MonsterShatter.Death (64 bytes).
struct Death
{
    float3 position; float yaw;   // the body's placement on its break (the crowd's MonsterInstance)
    float3 hit; float scale;      // where the blow landed (world)
    float2 blow; float force; float time; // the blow on the ground (unit or zero), its launch distance (m), the break's song time
    uint tint; uint seed; uint crowdSlot; uint slot; // gem colour; per-death hash seed; palette slot; ring slot
};

// Matches MonsterShatter.ShardStride (80 bytes).
struct Shard
{
    float3 position; float time;  // its pivot (world); the song time it is integrated to
    float4 rotation;              // its turn since the break (a quaternion)
    float3 velocity; float sinkFrom; // m/s; its height when the sink began, or -1
    float3 spin; float radius;    // rad/s (world); its contact radius on the ground (m)
    float3 origin; float pad;     // its pivot on the break (world)
};

// Matches CrowdShard (48 bytes).
struct ShardRest
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
