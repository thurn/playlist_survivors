// The world's globals (World.cs publishes them every frame; design.md, The Look > The beat wave, Restraint; The World >
// Zones), for every shader that reacts to the world: the grid floor (GridFloor.shader), the monsters' lines (a flare
// and a bob as the beat wave passes them), the structures' edges and fills (the flare, the zone key). Zero rings until
// a world publishes, so any shader may include this.
//
// - The beat wave (BeatWave.cs): a ring from the hero's feet on every reliable beat, its front at 11 m a beat, a sharp
//   leading edge and a wake gone within half a beat, a downbeat's at full strength. None beatless.
// - Event rings (ImpulseField.cs): on-beat dashes, slams, boss entrances, mass kills, breaks; only a special moment's
//   ring is drawn at full strength (and lights _RC_Special), and only rings past a knee bend the grid.
// - The zone key (ZoneSweep.cs): the floor's colour family, crossfading from _RC_ZoneFrom to _RC_ZoneTo behind a front
//   swept out from the hero over a beat.
#ifndef CHAMPIONS_WORLD_INCLUDED
#define CHAMPIONS_WORLD_INCLUDED

#define BEAT_RING_MAX 4
#define IMPULSE_MAX 16

float4 _RC_BeatRing[BEAT_RING_MAX];  // centre x, z (m); the front's radius (m); strength now (1: a downbeat's front)
float4 _RC_BeatShape;                // front width, wake width (m); a monster's bob at full strength (m); 0
float _RC_BeatRingCount;
float4 _RC_Clock;                    // song time; the beat's phase (0 to 1, 0 beatless); seconds since the beat; its period

float4 _RC_ImpulseRing[IMPULSE_MAX];  // centre x, z; the front's radius; the band's width (m)
float4 _RC_ImpulseColor[IMPULSE_MAX]; // linear colour times strength, strength
float _RC_ImpulseCount;
// The special-moment envelope (design.md, Restraint): 1 on a special moment's first frame, gone within a quarter second.
float _RC_Special;

float4 _RC_ZoneFrom;   // the key before the latest change (linear rgb)
float4 _RC_ZoneTo;     // the key now (linear rgb)
float4 _RC_ZoneSweep;  // the change's centre x, z; its front's radius (m; past the frame once done); the feather (m)

float4 _RC_Hero;       // the hero's feet x, z; song time; 0
// The look metrics' ID render (champions look): 1 while it draws, when the world draws black.
float _RC_IdPass;

// A flash's gain: `calm` outside special moments, full in one. Every flash in the look goes through it.
float LookFlash(float calm) { return lerp(calm, 1, saturate(_RC_Special)); }

// One beat ring's band at signed distance x from its front (> 0 ahead): 1 on the front.
float BeatBand(float x)
{
    float w = x > 0 ? _RC_BeatShape.x : _RC_BeatShape.y;
    return exp(-x * x / max(1e-4, w * w));
}

// The beat wave at ground point xz: its strength (0 to 1, a downbeat's front 1) and the push outward from each ring's
// centre (m per unit strength): the ripple that bends the grid's lines (Geometry Wars' grid).
void BeatWave(float2 xz, out float strength, out float2 push)
{
    strength = 0;
    push = 0;
    int n = (int)_RC_BeatRingCount;
    for (int i = 0; i < n; i++)
    {
        float4 ring = _RC_BeatRing[i];
        float2 off = xz - ring.xy;
        float d = length(off);
        float s = ring.w * BeatBand(d - ring.z);
        strength += s;
        push += (d > 1e-3 ? off / d : 0) * s;
    }
    strength = saturate(strength);
}

float BeatWaveAt(float2 xz)
{
    float strength;
    float2 push;
    BeatWave(xz, strength, push);
    return strength;
}

// The hook for the monsters' lines: one standing at rootXZ as the wave passes, its flare (0 to 1: how far its lines
// brighten) and its bob (m, up).
void BeatWaveCrowd(float2 rootXZ, out float flare, out float bob)
{
    flare = BeatWaveAt(rootXZ);
    bob = _RC_BeatShape.z * flare;
}

// One event ring's band at distance d from its centre: a sharp leading edge and a softer wake, 1 on the front.
float ImpulseBand(float d, float4 ring)
{
    float x = d - ring.z;
    float w = x > 0 ? ring.w * 0.25 : ring.w * 0.6;
    return exp(-x * x / max(1e-4, w * w));
}

// Every event ring at ground point xz past a knee: each ring's strength less `knee`, rescaled so a full-strength ring
// keeps its own, so rings weaker than the knee add nothing (the floor's: only special moments bend the grid). The
// strength there (summed), the light (linear) and the push outward (m per unit strength).
void ImpulseAtKnee(float2 xz, float knee, out float strength, out float3 light, out float2 push)
{
    strength = 0;
    light = 0;
    push = 0;
    int n = (int)_RC_ImpulseCount;
    for (int i = 0; i < n; i++)
    {
        float4 ring = _RC_ImpulseRing[i];
        float2 off = xz - ring.xy;
        float d = length(off);
        float s = _RC_ImpulseColor[i].w;
        float past = max(0, s - knee) / (1 - knee);
        float band = ImpulseBand(d, ring);
        strength += band * past;
        light += band * _RC_ImpulseColor[i].rgb * (past / max(s, 1e-4));
        push += (d > 1e-3 ? off / d : 0) * band * past;
    }
}

void ImpulseAt(float2 xz, out float strength, out float3 light, out float2 push)
{
    ImpulseAtKnee(xz, 0, strength, light, push);
}

// How far ground point xz has taken the latest zone key: 0 the key before, 1 the key now.
float ZoneNew(float2 xz)
{
    float d = length(xz - _RC_ZoneSweep.xy);
    return saturate((_RC_ZoneSweep.z - d) / max(1e-3, _RC_ZoneSweep.w) + 0.5);
}

// The zone key at ground point xz (linear rgb): a structure's fill takes it, and the grid's lines its hue.
float3 ZoneKey(float2 xz) { return lerp(_RC_ZoneFrom.rgb, _RC_ZoneTo.rgb, ZoneNew(xz)); }

#endif
