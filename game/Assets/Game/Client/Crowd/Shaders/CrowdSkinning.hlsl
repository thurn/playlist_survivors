// The crowd's skinning core, shared by the draw shader and the compute passes (no stage-specific
// code here): the instance struct, the bone texture's layout and the 2-clip × 2-frame blend.
// demo3d's CrowdSkinning.hlsl, copied and owned. The clip rate (CrowdLibrary.ClipRate) comes as a
// parameter.
#ifndef GAME_CROWD_SKINNING_INCLUDED
#define GAME_CROWD_SKINNING_INCLUDED

#define CROWD_ALIVE 1u
#define CROWD_DYING 2u
#define CROWD_HIDDEN 4u

// Matches Game.Client.Crowd.MonsterInstance (64 bytes).
struct MonsterInstance
{
    float3 position; float yaw;
    uint clipA; float timeA; uint clipB; float timeB;
    float blend; float scale; float hitFlash; float dissolve;
    uint tint; uint flags; float2 pad;
};

StructuredBuffer<float4> _ClipTable;     // per clip: firstRow, frames, length, loop
Texture2D<float4> _BoneTex;              // bone b's row r of frame f is texel (3b + r, f)
StructuredBuffer<float4> _ClipBounds;    // per clip: centre height, radius, height at instance scale 1

// A monster's bounding sphere: its clip's, or one holding both clips' while they crossfade:
// xyz centre, w radius; `height` gets the taller clip's height.
float4 CrowdSphere(MonsterInstance m, out float height)
{
    float4 b = _ClipBounds[m.clipA];
    if (m.blend < 0.9999)
    {
        float4 o = _ClipBounds[m.clipB];
        float cy = 0.5 * (b.x + o.x);
        b = float4(cy, max(abs(cy - b.x) + b.y, abs(cy - o.x) + o.y), max(b.z, o.z), 0);
    }
    height = b.z * m.scale;
    return float4(m.position + float3(0, b.x * m.scale, 0), b.y * m.scale);
}

// The two texture rows around a clip time and the fraction between them.
void ClipRows(uint clip, float t, float rate, out uint row0, out uint row1, out float frac)
{
    float4 c = _ClipTable[clip];
    float len = c.z;
    if (c.w > 0.5) t = len > 0 ? t - floor(t / len) * len : 0;
    else t = clamp(t, 0, len);
    uint frames = (uint)c.y;
    uint f0 = min((uint)(t * rate), frames - 2);
    float t0 = f0 / rate;
    float t1 = min((f0 + 1) / rate, len);
    frac = saturate((t - t0) / max(t1 - t0, 1e-5));
    row0 = (uint)c.x + f0;
    row1 = row0 + 1;
}

float3x4 LoadBone(uint row, uint bone)
{
    int x = (int)(bone * 3);
    return float3x4(_BoneTex.Load(int3(x, row, 0)), _BoneTex.Load(int3(x + 1, row, 0)), _BoneTex.Load(int3(x + 2, row, 0)));
}

struct ClipSample { uint a0, a1, b0, b1; float fa, fb, blend; };

ClipSample SampleOf(MonsterInstance m, float rate)
{
    ClipSample s;
    ClipRows(m.clipA, m.timeA, rate, s.a0, s.a1, s.fa);
    ClipRows(m.clipB, m.timeB, rate, s.b0, s.b1, s.fb);
    s.blend = saturate(m.blend);
    return s;
}

// One bone's matrix: 2 clips × 2 frames (12 texel loads), crossfaded. The second clip is skipped
// when it has no weight.
float3x4 BlendBone(ClipSample s, uint bone)
{
    float3x4 a = lerp(LoadBone(s.a0, bone), LoadBone(s.a1, bone), s.fa);
    [branch]
    if (s.blend < 0.9999)
    {
        float3x4 b = lerp(LoadBone(s.b0, bone), LoadBone(s.b1, bone), s.fb);
        a = a * s.blend + b * (1 - s.blend);
    }
    return a;
}

#endif
