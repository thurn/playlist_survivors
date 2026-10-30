// What every StyleLab shader shares: the beat globals, the style mesh's extra vertex data,
// noise, the beat wave, lighting helpers and the death effects (stylelab.md, Shaders).
#ifndef STYLELAB_COMMON_INCLUDED
#define STYLELAB_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Set every frame by Beat.cs.
float _SL_Time;      // the song's time, seconds
float _SL_Beat;      // beats since the song's first beat, fractional
float _SL_Pulse;     // 1 on each beat, decaying
float _SL_BarPulse;  // 1 on each downbeat, decaying
float _SL_Energy;    // the song map's energy at this bar, 0..1
float4 _SL_HeroPos;  // the hero's feet, world space
float _SL_WaveSpeed; // how far the beat wave travels in one beat, metres
float _SL_RingGain;  // the beat ring drawn on the ground (0 in the baseline)

// Per-monster values, from each renderer's MaterialPropertyBlock (Monster.cs).
#define SL_INSTANCE_PROPS float _Hit; float _Death; float _Seed; float4 _Center; float _HeightM;

// The style mesh (StyleMesh.cs) is unwelded: every triangle has its own three vertices.
struct SLAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;  // the triangle's face normal, skinned like a normal
    float2 uv : TEXCOORD0;
    float4 bary : TEXCOORD2;     // barycentric xyz, and a random value per triangle in w
    float4 rest : TEXCOORD3;     // the bind-pose position in metres, the type's root space
};

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

Light SL_MainLight(float3 positionWS)
{
    return GetMainLight(TransformWorldToShadowCoord(positionWS));
}

float SL_Ao(float4 positionCS)
{
#if defined(_SCREEN_SPACE_OCCLUSION)
    return GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(positionCS)).indirectAmbientOcclusion;
#else
    return 1.0;
#endif
}

// Every additional light (the hero's torch, the crystals' glow): wrapped diffuse and a
// Blinn-Phong highlight of the given power, summed.
void SL_AdditionalLights(float3 positionWS, float4 positionCS, float3 N, float3 V, float wrap,
                         float specPower, inout float3 diffuse, inout float3 specular)
{
#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = N;
    inputData.viewDirectionWS = V;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        float3 c = light.color * light.distanceAttenuation * light.shadowAttenuation;
        float nl = dot(N, light.direction);
        diffuse += c * saturate((nl + wrap) / (1.0 + wrap));
        float3 H = normalize(light.direction + V);
        specular += c * pow(saturate(dot(N, H)), specPower) * saturate(nl * 4.0);
    LIGHT_LOOP_END
#endif
}

// ---------------------------------------------------------------------------------------------
// Death.

// Triangles fly off along their face normals and fall, staggered by their random value.
// Returns how far through its flight the triangle is (1: gone).
float SL_Shatter(inout float3 positionWS, float3 faceNormalWS, float triRand, float death, float strength)
{
    float t = saturate(death * 1.6 - triRand * 0.6);
    float3 dir = normalize(faceNormalWS + float3(0, 0.9, 0) + (SL_Hash3(triRand * 37.0) - 0.5));
    positionWS += dir * t * strength * (0.7 + triRand) + float3(0, -3.0, 0) * t * t * strength * 0.5;
    return t;
}

// Burns away through cellular noise on the rest position. Returns the hot edge (0..1); clips
// what has burned.
float SL_Dissolve(float3 rest, float death)
{
    if (death <= 0.0) return 0.0;
    float n = SL_Noise(rest * 4.0) * 0.7 + SL_Noise(rest * 11.0) * 0.3;
    float cut = death * 1.15;
    clip(n - cut);
    return 1.0 - saturate((n - cut) / 0.08);
}

// The varyings most shaders pass on: a vertex shader displaces positionWS, then finishes here.
struct SLVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float4 bary : TEXCOORD3;
    float4 rest : TEXCOORD4;
    float3 faceNormalWS : TEXCOORD5;
    float fogFactor : TEXCOORD6;
    float4 extra : TEXCOORD7;  // a shader's own per-vertex values
};

SLVaryings SL_Finish(SLAttributes v, float3 positionWS, float3 normalWS)
{
    SLVaryings o;
    o.positionWS = positionWS;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = normalWS;
    o.uv = v.uv;
    o.bary = v.bary;
    o.rest = v.rest;
    o.faceNormalWS = TransformObjectToWorldNormal(v.tangentOS.xyz);
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    o.extra = 0;
    return o;
}

// A thin line along every triangle edge, about `pixels` wide.
float SL_Wire(float3 bary, float pixels)
{
    float e = min(bary.x, min(bary.y, bary.z));
    float w = fwidth(e) * pixels;
    return 1.0 - smoothstep(w * 0.5, w, e);
}

#endif
