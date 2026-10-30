// The effect runtime's shared definitions (effects.md, The runtime): the emitter definition's
// layout (mirrors EmitterSlots in EffectLibrary.cs), the particle and instance records, hashed
// randomness and the lookup rows. Our own code, written from scratch.
#ifndef DEMO3D_EFFECT_COMMON
#define DEMO3D_EFFECT_COMMON

#define EMITTER_STRIDE 28
#define LUT_ROWS 14
#define LUT_WIDTH 64

#define S_TIME 0
#define S_LIFE 1
#define S_SIZEMIN 2
#define S_SIZEMAX 3
#define S_ROTMIN 4
#define S_ROTMAX 5
#define S_COLA 6
#define S_COLB 7
#define S_SHAPE0 8
#define S_SHAPE1 9
#define S_SHAPE2 10
#define S_SHAPEPOS 11
#define S_SHAPEROT 12
#define S_CHILDPOS 13
#define S_CHILDROT 14
#define S_CHILDSCALE 15
#define S_NOISE 16
#define S_RENDER0 17
#define S_RENDER1 18
#define S_TEX 19
#define S_MAT0 20
#define S_MAT1 21
#define S_MAT2 22
#define S_MOD 23
#define S_SHAPESCALE 24
#define S_ORBITAL 25
#define S_HEAT0 26
#define S_HEAT1 27

#define R_COLOR 0
#define R_COLOR2 1
#define R_SIZE 2
#define R_SIZE2 3
#define R_VEL 4
#define R_VEL2 5
#define R_FORCE 6
#define R_FORCE2 7
#define R_MISC 8
#define R_CUSTOM 9
#define R_STARTCOLOR 10
#define R_MISC2 11
#define R_ROT3 12
#define R_ORBITAL 13

#define F_LOOP 1u
#define F_LOCAL 2u
#define F_SIZE3D 4u
#define F_ROT3D 8u
#define F_VEL 16u
#define F_FORCE 32u
#define F_LIMIT 64u
#define F_NOISE 128u
#define F_COLOR2 256u
#define F_SIZEAXES 512u
#define F_VELWORLD 1024u
#define F_FORCEWORLD 2048u
#define F_COLORLIFE 4096u
#define F_SIZELIFE 8192u
#define F_ROTLIFE 16384u
#define F_SHEET 32768u
#define F_SIZESPEED 65536u
#define F_CUSTOM 131072u
#define F_STARTCOLOR2 262144u
#define F_STARTGRADIENT 524288u
#define F_VEL2 (1u << 20)
#define F_FORCE2 (1u << 21)
#define F_SIZE2 (1u << 22)
#define F_SHAPESCALEONLY (1u << 23)
#define F_SOFT (1u << 24)
#define F_NOISETEX (1u << 25)
#define F_RANDROTDIR (1u << 26)
#define F_BEAM (1u << 27)
#define F_SHEETROW (1u << 28)
#define F_ORBITAL (1u << 29)
#define F_ROTAXES (1u << 30)

#define RM_BILLBOARD 0
#define RM_STRETCH 1
#define RM_HORIZONTAL 2
#define RM_VERTICAL 3
#define RM_MESH 4
#define RM_BEAM 5

#define SHAPE_SPHERE 1
#define SHAPE_HEMISPHERE 2
#define SHAPE_CONE 3
#define SHAPE_CONEVOLUME 4
#define SHAPE_CIRCLE 5
#define SHAPE_BOX 6
#define SHAPE_EDGE 7

// Counters (uint), shared with EffectSystem.cs.
#define C_ALIVE 0
#define C_DEAD 1
#define C_ALLOC 2
#define C_REJECT 3
#define C_NEXT 4
#define C_EMITTED 5
#define C_BUCKET 8
#define MAX_BUCKETS 24

struct Particle
{
    float4 posAge;   // position (emitter-local for local-space emitters, else world), age in s (negative: not born)
    float4 velLife;  // velocity, lifetime in s
    float4 rotInst;  // rotation xyz in radians, instance ring index (asfloat)
    float4 sizeSeed; // start size xyz, seed (asfloat)
    float4 color;    // start colour
    float4 misc;     // emitter (asfloat), start frame, random for two-row curves, birth time
    float4 vEff;     // last step's displacement velocity (world for world-space emitters), for stretching
};

struct Instance
{
    float4 posYaw;   // origin, yaw (radians)
    float4 velScale; // origin velocity, scale
    float4 timing;   // spawn time, yaw rate, emitter (asfloat), seed (asfloat)
    float4 seg[4];   // emission segments: first particle, particles per step, first birth, step seconds
    float4 alloc;    // count (asfloat), merged density (TD11's merge cut; 1 or more), allocation offset (asfloat; ~0 rejected), duration
    float4 tint;     // the spawn's own heat colour, at weight a (0: the emitter's ramp); a crystal death's gem
};

StructuredBuffer<float4> _Emitters;
Texture2D<float4> _Lut;
float4 _LutSize; // width, height, 1/width, 1/height

float4 EM(uint e, uint slot) { return _Emitters[e * EMITTER_STRIDE + slot]; }
uint EFlags(uint e) { return asuint(EM(e, S_TIME).w); }
uint ERow(uint e) { return (uint)EM(e, S_CHILDSCALE).w; }

// One row sample with linear filtering over 64 samples; u in [0, 1].
float4 LutRow(uint row, float u)
{
    float x = saturate(u) * (LUT_WIDTH - 1);
    uint i = min((uint)x, LUT_WIDTH - 2);
    float f = x - i;
    float4 a = _Lut.Load(int3(i, row, 0));
    float4 b = _Lut.Load(int3(i + 1, row, 0));
    return lerp(a, b, f);
}

float4 Lut(uint e, uint r, float u) { return LutRow(ERow(e) + r, u); }

// Two-row curves (two curves, two gradients): a per-particle lerp between them.
float4 Lut2(uint e, uint r, uint r2, bool two, float u, float rnd)
{
    float4 a = Lut(e, r, u);
    return two ? lerp(a, Lut(e, r2, u), rnd) : a;
}

// PCG hash (Jarzynski and Olano 2020).
uint Pcg(uint v)
{
    uint state = v * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

uint Hash3(uint a, uint b, uint c) { return Pcg(a ^ Pcg(b ^ Pcg(c))); }

float Rand(uint seed, uint salt) { return (Pcg(seed ^ Pcg(salt)) >> 8) * (1.0 / 16777216.0); }

float3 RotateQ(float4 q, float3 v)
{
    float3 t = 2 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float3 RotateY(float yaw, float3 v)
{
    float s, c;
    sincos(yaw, s, c);
    return float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
}

// Unity's Euler order: Z, then X, then Y (rotation applied to v as Y * X * Z).
float3 RotateEuler(float3 r, float3 v)
{
    float s, c;
    sincos(r.z, s, c); v = float3(c * v.x - s * v.y, s * v.x + c * v.y, v.z);
    sincos(r.x, s, c); v = float3(v.x, c * v.y - s * v.z, s * v.y + c * v.z);
    sincos(r.y, s, c); v = float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
    return v;
}

// The emitter's frame in world space at time t: origin moving with the instance's velocity,
// yawed by the instance's yaw (and yaw rate), scaled, then the child's own transform.
struct Frame { float3 origin; float yaw; float scale; float4 childRot; float3 childPos; float3 childScale; };

Frame EmitterFrame(Instance inst, uint e, float t)
{
    Frame f;
    float dt = t - inst.timing.x;
    f.origin = inst.posYaw.xyz + inst.velScale.xyz * dt;
    f.yaw = inst.posYaw.w + inst.timing.y * dt;
    f.scale = inst.velScale.w;
    f.childRot = EM(e, S_CHILDROT);
    f.childPos = EM(e, S_CHILDPOS).xyz;
    f.childScale = EM(e, S_CHILDSCALE).xyz;
    return f;
}

// Emitter-local point to world.
float3 FramePoint(Frame f, float3 p)
{
    return f.origin + RotateY(f.yaw, (f.childPos + RotateQ(f.childRot, p * f.childScale)) * f.scale);
}

// Emitter-local direction to world (scaled).
float3 FrameVector(Frame f, float3 v)
{
    return RotateY(f.yaw, RotateQ(f.childRot, v * f.childScale) * f.scale);
}

float3 FrameInverseVector(Frame f, float3 w)
{
    float3 v = RotateY(-f.yaw, w) / max(f.scale, 1e-4);
    float4 qi = float4(-f.childRot.xyz, f.childRot.w);
    return RotateQ(qi, v) / max(f.childScale, 1e-4);
}

#endif
