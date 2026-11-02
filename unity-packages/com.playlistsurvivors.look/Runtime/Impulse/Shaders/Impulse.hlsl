// The impulse field's rings (neon-look.md, The impulse field; ImpulseField.cs publishes them): every blow, death,
// shatter and beat a ring expanding over the ground. The floor grid bends and brightens as one passes
// (GridFloor.shader); a crowd look can wobble and light up its monsters with ImpulseCrowd; the post can ripple the
// screen round _ImpulseStrongest. Globals, so any shader may include this; zero rings until a player publishes.
#ifndef PS_LOOK_IMPULSE_INCLUDED
#define PS_LOOK_IMPULSE_INCLUDED

#define IMPULSE_MAX 32

float4 _ImpulseRing[IMPULSE_MAX];   // centre x, z, the front's radius, the band's width (m)
float4 _ImpulseColor[IMPULSE_MAX];  // linear colour times strength, strength
float _ImpulseCount;
float4 _ImpulseStrongest;           // the strongest ring: centre x, z, radius, strength (0: none)
// The special-moment tier (neon-look.md, Restraint and special moments): 1 on a special moment's first frame (a
// structure break, a formation smash, a slam), falling to 0 over ImpulseField.SpecialSeconds; 0 outside one.
float _LookSpecial;

// A flash's gain: `calm` outside special moments, full in one. Every look shader's flash goes through it.
float LookFlash(float calm) { return lerp(calm, 1, saturate(_LookSpecial)); }
// The monsters' hit flash and their shards' break flash outside special moments.
#define LOOK_HIT_CALM 0.35
#define LOOK_SHARD_CALM 0.5

// One ring's band at distance d from its centre: a sharp leading edge and a softer wake, 1 on the front.
float ImpulseBand(float d, float4 ring)
{
    float x = d - ring.z;
    float w = x > 0 ? ring.w * 0.25 : ring.w * 0.6;
    return exp(-x * x / max(1e-4, w * w));
}

// Every ring at ground point xz past a knee: each ring's strength less `knee` (of 1), rescaled so a full-strength
// ring keeps its own, so rings weaker than the knee add nothing (the floor's: only big impacts bend the grid). The
// strength there (summed), the light (linear) and the push outward (m, the warp that bends the grid's lines away from
// each ring's centre, Geometry Wars' grid).
void ImpulseAtKnee(float2 xz, float knee, out float strength, out float3 light, out float2 push)
{
    strength = 0;
    light = 0;
    push = 0;
    int n = (int)_ImpulseCount;
    for (int i = 0; i < n; i++)
    {
        float4 ring = _ImpulseRing[i];
        float2 off = xz - ring.xy;
        float d = length(off);
        float s = _ImpulseColor[i].w;
        float past = max(0, s - knee) / (1 - knee);
        float band = ImpulseBand(d, ring);
        strength += band * past;
        light += band * _ImpulseColor[i].rgb * (past / max(s, 1e-4));
        push += (d > 1e-3 ? off / d : 0) * band * past;
    }
}

// Every ring at ground point xz: the strength there (summed), the light (linear) and the push outward (m).
void ImpulseAt(float2 xz, out float strength, out float3 light, out float2 push)
{
    ImpulseAtKnee(xz, 0, strength, light, push);
}

// The hook for crowd looks: a monster standing at rootXZ as rings pass, its hop (m, up) and its light (linear).
void ImpulseCrowd(float2 rootXZ, out float hop, out float3 light)
{
    float strength;
    float2 push;
    ImpulseAt(rootXZ, strength, light, push);
    hop = 0.12 * saturate(strength);
}

#endif
