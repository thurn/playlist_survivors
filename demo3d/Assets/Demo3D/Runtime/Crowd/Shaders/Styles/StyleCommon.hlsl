// What every `--monster-style` shader shares (crowd.md, Monster styles): stylelab's
// StyleLabCommon.hlsl ported onto the bone-texture crowd. The beat globals, the skinned vertex
// front end and per-monster values from the instance buffer, noise, the beat wave, lighting
// helpers and death. A style shader declares its UnityPerMaterial CBUFFER (ending with
// `half _Gain`, the treatment's tuned scale on its forward colour (MonsterStyle.Gain), and the type's
// `float _HeightM`, CrowdType.HeightM: its height at instance scale 1) before including this, as
// CrowdSkinned does before CrowdCommon.hlsl, in the SubShader's HLSLINCLUDE so its shadow,
// depth and motion passes (StylePasses.hlsl) see the same displacement and death.
#ifndef DEMO3D_STYLE_COMMON_INCLUDED
#define DEMO3D_STYLE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "../CrowdCommon.hlsl"
#include "../../../Arena/Shaders/StaticShadow.hlsl"

// Set every frame by StyleBeat.cs from the song clock, the song map and the hero.
float _SL_Time;      // the song's time, seconds
float _SL_Beat;      // beats since the song's first beat, fractional
float _SL_Pulse;     // 1 on each beat, decaying
float _SL_BarPulse;  // 1 on each downbeat, decaying
float _SL_Energy;    // the song map's energy at this beat, 0..1
float4 _SL_HeroPos;  // the hero's feet, world space
float _SL_WaveSpeed; // how far the beat wave travels in one beat, metres
float _SL_RingGain;  // the lab's beat ring on the ground: always 0 here (demo3d's ground has none)

// The crowd's welded mesh (CrowdAttributes): bone data in TEXCOORD1/2. A style that draws the
// unwelded style mesh (CrowdBaker.Unweld, MonsterStyle.UsesStyleMesh) defines SL_STYLE_MESH before
// including this, and gets its extras: the face normal (SL_SkinFace) and barycentrics.
struct SLAttributes
{
    float3 positionOS : POSITION;  // root space at instance scale 1, metres: also the rest position
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float4 boneIndex : TEXCOORD1;
    float4 boneWeight : TEXCOORD2;
#if defined(SL_STYLE_MESH)
    float4 tangentOS : TANGENT;  // the triangle's face normal
    float4 bary : TEXCOORD3;     // barycentric xyz, and a random value per triangle in w
#endif
    uint instanceID : SV_InstanceID;
};

#if defined(SL_STYLE_MESH)
// The style mesh's face normal in world space, turned by the vertex's bones and the instance's yaw
// as its normal is (this frame's bones, also for the previous pose: one frame's turn of a flying
// shard's direction doesn't show).
float3 SL_SkinFace(SLAttributes v, MonsterInstance m, uint slot)
{
    float3 p, n;
    SkinVertex(m, slot, false, v.positionOS, v.tangentOS.xyz, v.boneIndex, v.boneWeight, p, n);
    return n;
}
#endif

// ---------------------------------------------------------------------------------------------
// Noise.

float SL_Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SL_Hash1(float n) { return frac(sin(n) * 43758.5453); }

float3 SL_Hash3(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453);
}

float SL_Noise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(SL_Hash(i), SL_Hash(i + float3(1, 0, 0)), f.x),
                     lerp(SL_Hash(i + float3(0, 1, 0)), SL_Hash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(SL_Hash(i + float3(0, 0, 1)), SL_Hash(i + float3(1, 0, 1)), f.x),
                     lerp(SL_Hash(i + float3(0, 1, 1)), SL_Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float SL_Fbm(float3 p)
{
    float sum = 0.0, amp = 0.5;
    for (int i = 0; i < 4; i++)
    {
        sum += amp * SL_Noise(p);
        p = p * 2.03 + 17.1;
        amp *= 0.5;
    }
    return sum;
}

// Cellular noise: x the distance to the nearest cell point, y the gap to the second (0 on edges).
float2 SL_Voronoi(float3 x)
{
    float3 p = floor(x);
    float3 f = frac(x);
    float d1 = 8.0, d2 = 8.0;
    for (int k = -1; k <= 1; k++)
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++)
    {
        float3 b = float3(i, j, k);
        float3 r = b - f + SL_Hash3(p + b);
        float d = dot(r, r);
        if (d < d1) { d2 = d1; d1 = d; }
        else if (d < d2) { d2 = d; }
    }
    d1 = sqrt(d1);
    return float2(d1, sqrt(d2) - d1);
}

float3 SL_Hue(float h)
{
    float3 k = float3(1.0, 2.0 / 3.0, 1.0 / 3.0);
    return saturate(abs(frac(h + k) * 6.0 - 3.0) - 1.0);
}

float SL_Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// ---------------------------------------------------------------------------------------------
// The beat wave: a ring that leaves the hero on every beat and sweeps out through the horde.
// 1 where the ring is passing now, decaying behind it (sharpness: how fast).

float SL_Wave(float3 positionWS, float sharpness)
{
    float d = distance(positionWS.xz, _SL_HeroPos.xz);
    float phase = frac(_SL_Beat - d / max(_SL_WaveSpeed, 0.01));
    return exp(-phase * sharpness);
}

// The wave's distance-delayed beat count, for things that change once per beat as it passes.
float SL_WaveBeat(float3 positionWS)
{
    float d = distance(positionWS.xz, _SL_HeroPos.xz);
    return floor(_SL_Beat - d / max(_SL_WaveSpeed, 0.01));
}

// ---------------------------------------------------------------------------------------------
// Lighting.

// The moon, its realtime shadow combined with the arena's static shadow map as the crowd's is.
Light SL_MainLight(float3 positionWS)
{
    Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
    light.shadowAttenuation = min(light.shadowAttenuation, Demo3DStaticShadow(positionWS));
    return light;
}

float SL_Ao(float4 positionCS)
{
#if defined(_SCREEN_SPACE_OCCLUSION)
    return GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS)).indirectAmbientOcclusion;
#else
    return 1.0;
#endif
}

void SL_AddLight(Light light, float3 N, float3 V, float wrap, float specPower, inout float3 diffuse, inout float3 specular)
{
    float3 c = light.color * light.distanceAttenuation * light.shadowAttenuation;
    float nl = dot(N, light.direction);
    diffuse += c * saturate((nl + wrap) / (1.0 + wrap));
    float3 H = normalize(light.direction + V);
    specular += c * pow(saturate(dot(N, H)), specPower) * saturate(nl * 4.0);
}

// Every additional light (the hero's torch, the effects' pooled lights) through Forward+'s
// cluster loop: wrapped diffuse and a Blinn-Phong highlight of the given power, summed.
void SL_AdditionalLights(float3 positionWS, float4 positionCS, float3 N, float3 V, float wrap,
                         float specPower, inout float3 diffuse, inout float3 specular)
{
#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = N;
    inputData.viewDirectionWS = V;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        SL_AddLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), N, V, wrap, specPower, diffuse, specular);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        SL_AddLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), N, V, wrap, specPower, diffuse, specular);
    LIGHT_LOOP_END
#endif
}

// ---------------------------------------------------------------------------------------------
// Death.

// How far through its flight a shattering triangle is (SL_Shatter's value, also in a fragment).
float SL_ShatterT(float death, float triRand) { return saturate(death * 1.6 - triRand * 0.6); }

// Triangles fly off along their face normals and fall, staggered by their random value.
// Returns how far through its flight the triangle is (1: gone).
float SL_Shatter(inout float3 positionWS, float3 faceNormalWS, float triRand, float death, float strength)
{
    float t = SL_ShatterT(death, triRand);
    float3 dir = normalize(faceNormalWS + float3(0, 0.9, 0) + (SL_Hash3(triRand * 37.0) - 0.5));
    positionWS += dir * t * strength * (0.7 + triRand) + float3(0, -3.0, 0) * t * t * strength * 0.5;
    return t;
}

// Burns away through noise on the rest position: the baseline's own dissolve (CrowdDissolve).
// Every pass clips with it (StylePasses.hlsl). Returns the hot edge (0..1).
float SL_Dissolve(float3 rest, float death)
{
    return CrowdDissolve(death, rest);
}

// A style whose death is not the baseline's dissolve (fire burns from the feet, chrome melts)
// defines SL_DISSOLVE(rest, death) before including this, returning the hot edge (0..1) and
// clipping as it likes; its function must be declared before the passes (the HLSLINCLUDE, after
// this file). The forward pass (SL_FRAGMENT_BEGIN) and every other pass clip with it.
#ifndef SL_DISSOLVE
#define SL_DISSOLVE(rest, death) SL_Dissolve(rest, death)
#endif

#if defined(SL_STYLE_MESH)
// SL_Shatter for SL_DISPLACE: a dying monster's triangles fly apart. One that has flown its course
// collapses to a point, so no pass draws it, and such a style defines SL_DISSOLVE as 0.0.
void SL_ShatterDisplace(SLAttributes v, MonsterInstance m, uint slot, inout float3 positionWS, float strength)
{
    UNITY_BRANCH if (m.dissolve <= 0.0) return;
    if (SL_Shatter(positionWS, SL_SkinFace(v, m, slot), v.bary.w, m.dissolve, strength) >= 0.98)
        positionWS = m.position;
}
#endif

// ---------------------------------------------------------------------------------------------
// Skinning.

// The lab's per-monster MaterialPropertyBlock values, from the instance: hit flash and death
// (MonsterInstance.hitFlash, dissolve), a seed from the slot (stable while the monster lives),
// the centre (its feet) and the height (scaled).
struct SLVertex
{
    MonsterInstance m;
    uint slot;
    float3 positionWS;
    float3 normalWS;
    float4 fx;      // x hit, y death, zw the LOD fade (CrowdLodFade)
    float4 center;  // xyz the feet, w the seed (0..1)
};

// A style that moves vertices (shatter, flames, melt) defines SL_DISPLACE(v, m, slot, positionWS,
// normalWS) before including this: every pass then skins through it, so shadows, depth and motion
// vectors follow the displaced mesh. m is the instance (the previous frame's, for motion vectors).
// It expands here, after the helpers above, so it may use them, the beat globals and the CBUFFER.
#ifndef SL_DISPLACE
#define SL_DISPLACE(v, m, slot, positionWS, normalWS)
#endif

// A monster's seed (0..1), from its slot: stable while it lives.
float SL_SeedOf(uint slot) { return frac(slot * 0.6180339887 + 0.137); }

SLVertex SL_Skin(SLAttributes v)
{
    SLVertex o;
    o.slot = CrowdSlot(v.instanceID);
    o.m = _Instances[o.slot];
    SkinVertex(o.m, o.slot, false, v.positionOS, v.normalOS, v.boneIndex, v.boneWeight, o.positionWS, o.normalWS);
    o.fx = float4(o.m.hitFlash, o.m.dissolve, CrowdLodFade(o.slot));
    o.center = float4(o.m.position, SL_SeedOf(o.slot));
    SL_DISPLACE(v, o.m, o.slot, o.positionWS, o.normalWS);
    return o;
}

// ---------------------------------------------------------------------------------------------
// The varyings most shaders pass on: a vertex shader displaces positionWS, then finishes here.

struct SLVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float3 rest : TEXCOORD3;
    nointerpolation float4 fx : TEXCOORD4;      // SLVertex.fx
    nointerpolation float4 center : TEXCOORD5;  // SLVertex.center
    float fogFactor : TEXCOORD6;
    float4 extra : TEXCOORD7;  // a shader's own per-vertex values
#if defined(SL_STYLE_MESH)
    float4 bary : TEXCOORD8;   // SLAttributes.bary
#endif
};

SLVaryings SL_Finish(SLAttributes v, SLVertex c, float3 positionWS, float3 normalWS)
{
    SLVaryings o;
    o.positionWS = positionWS;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = normalWS;
    o.uv = v.uv;
    o.rest = v.positionOS;
    o.fx = c.fx;
    o.center = c.center;
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    o.extra = 0;
#if defined(SL_STYLE_MESH)
    o.bary = v.bary;
#endif
    return o;
}

#define SL_HIT(i) ((i).fx.x)
#define SL_DEATH(i) ((i).fx.y)
#define SL_SEED(i) ((i).center.w)

// A thin line along every triangle edge of the style mesh, about `pixels` wide.
float SL_Wire(float3 bary, float pixels)
{
    float e = min(bary.x, min(bary.y, bary.z));
    float w = fwidth(e) * pixels;
    return 1.0 - smoothstep(w * 0.5, w, e);
}

// Every style fragment starts here: the LOD crossfade's dither, the death's clip (returning its
// hot edge) and TD15's ID render, which draws the crowd flat white (DemoMain.MaskMode).
#define SL_FRAGMENT_BEGIN(i, edge) \
    CrowdLodClip((i).fx.zw, (i).positionCS); \
    float edge = SL_DISSOLVE((i).rest, SL_DEATH(i)); \
    if (_Demo3DIdMask > 0.5) return half4(1, 1, 1, 1);

#endif
