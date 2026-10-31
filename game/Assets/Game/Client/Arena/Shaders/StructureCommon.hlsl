// What the crystal structures' shader shares with the style it came from (unity-game.md, "The
// Look"): demo3d's StyleCommon.hlsl under SL_STATIC, copied and owned for static meshes only. The
// beat globals, a structure's vertex front end and per-renderer values, noise, the beat wave, the
// lighting helpers, the facets and the set's grade. The shader declares its UnityPerMaterial
// CBUFFER (ending with `half _Gain; half _Peak;` and `float _HeightM`) before including this, in
// its SubShader's HLSLINCLUDE, so its shadow pass sees the same clip.
//
// A structure's piece values come per renderer (StructureStyle's MaterialPropertyBlock): its base
// and seed (_SL_Piece), its hit and break (_SL_PieceFx), a shard's rest matrix (_SL_Rest) and its
// site's gem colour (_SL_Tint). Alpha clip and the canopy fade follow the kit material it replaces.
#ifndef GAME_STRUCTURE_COMMON_INCLUDED
#define GAME_STRUCTURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "StaticShadow.hlsl"
#include "CanopyFade.hlsl"

// The beat globals, shared with the crowd's styles (demo3d's StyleBeat publishes them from the music
// each frame; ArenaRuntime sets them at rest until the look's beat feel drives them).
float _SL_Time;      // the song's time, seconds
float _SL_Beat;      // beats since the song's first beat, fractional
float _SL_Pulse;     // 1 on each beat, decaying
float _SL_BarPulse;  // 1 on each downbeat, decaying
float _SL_Energy;    // the music's energy at this beat, 0..1
float4 _SL_HeroPos;  // the hero's feet, world space
float _SL_WaveSpeed; // how far the beat wave travels in one beat, metres

// A structure's static mesh, in its renderer's object space. A shard's has its inside faces' flag in uv1.x.
struct SLAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
};

// Per renderer: the piece's base (xyz: its bounds' centre at their floor, world) and seed (w, 0..1);
// its hit flash (x) and death (y), 0 while it stands; z 1 on a broken piece's shard, whose rest
// position is where it stood in the piece (_SL_Rest, the shard's object-to-world matrix at the
// break), so a flying shard keeps the patterns it had; w its break glow (0..1), which lifts the
// set's grade (SL_Grade). _SL_Tint: the site's gem colour (linear; alpha 0 keeps the material's _Tint).
float4 _SL_Piece;
float4 _SL_PieceFx;
float4x4 _SL_Rest;
float4 _SL_Tint;
// Per material, from the kit material it replaces: its albedo and cutoff for alpha clip (0: none),
// and 1 to dither out near the hero as tree canopies do (CanopyFade.hlsl).
TEXTURE2D(_SL_ClipMap); SAMPLER(sampler_SL_ClipMap);
float4 _SL_ClipMap_ST;
float _SL_Cutoff;
float _SL_Canopy;
// SL_StaticFacets' cells per metre.
float _SL_CellScale;

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

float SL_Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// ---------------------------------------------------------------------------------------------
// The beat wave: a ring that leaves the hero on every beat and sweeps out across the arena.
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

// The moon, its realtime shadow combined with the arena's static shadow map.
Light SL_MainLight(float3 positionWS)
{
    Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
    light.shadowAttenuation = min(light.shadowAttenuation, GameStaticShadow(positionWS));
    return light;
}

void SL_AddLight(Light light, float3 N, float3 V, float wrap, float specPower, inout float3 diffuse, inout float3 specular)
{
    float3 c = light.color * light.distanceAttenuation * light.shadowAttenuation;
    float nl = dot(N, light.direction);
    diffuse += c * saturate((nl + wrap) / (1.0 + wrap));
    float3 H = normalize(light.direction + V);
    specular += c * pow(saturate(dot(N, H)), specPower) * saturate(nl * 4.0);
}

// Every additional light (the hero's light, the flames, the effects' lights) through Forward+'s
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
// The vertex front end: placed by its renderer, with the piece's values.

struct SLVertex
{
    float3 positionWS;
    float3 normalWS;
    float4 fx;      // x hit, y death, z 1 on a shard's inside face
    float4 center;  // xyz the piece's base, w its seed (0..1)
    float3 rest;    // metres from the piece's base
};

SLVertex SL_Skin(SLAttributes v)
{
    SLVertex o;
    o.positionWS = TransformObjectToWorld(v.positionOS);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    bool shard = _SL_PieceFx.z > 0.5;
    o.rest = (shard ? mul(_SL_Rest, float4(v.positionOS, 1)).xyz : o.positionWS) - _SL_Piece.xyz;
    o.fx = float4(_SL_PieceFx.xy, shard ? v.uv1.x : 0, 1);
    o.center = _SL_Piece;
    return o;
}

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
    float4 bary : TEXCOORD7;                     // SL_StaticFacets', filled in the fragment
};

SLVaryings SL_Finish(SLAttributes v, SLVertex c)
{
    SLVaryings o;
    o.positionWS = c.positionWS;
    o.positionCS = TransformWorldToHClip(c.positionWS);
    o.normalWS = c.normalWS;
    o.uv = v.uv;
    o.rest = c.rest;
    o.fx = c.fx;
    o.center = c.center;
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    o.bary = 0;
    return o;
}

#define SL_HIT(i) ((i).fx.x)
#define SL_DEATH(i) ((i).fx.y)
#define SL_INSIDE(i) ((i).fx.z)

// A thin line along every facet edge, about `pixels` wide.
float SL_Wire(float3 bary, float pixels)
{
    float e = min(bary.x, min(bary.y, bary.z));
    float w = fwidth(e) * pixels;
    return 1.0 - smoothstep(w * 0.5, w, e);
}

// A structure's clip in every pass but the shadow caster's: the kit material's alpha clip, and the
// canopy fade on trees (the shadow pass's view is the light's, which the fade must not read).
void SL_StaticClip(float4 positionCS, float3 positionWS, float2 uv, bool canopy)
{
    if (_SL_Cutoff > 0)
        clip(SAMPLE_TEXTURE2D(_SL_ClipMap, sampler_SL_ClipMap, uv * _SL_ClipMap_ST.xy + _SL_ClipMap_ST.zw).a - _SL_Cutoff);
    if (canopy && _SL_Canopy > 0.5) GameCanopyClip(positionCS, positionWS);
}

// Facets for a static mesh, whose triangles are the kit's (a few large ones per wall): world-space
// cellular cells of _SL_CellScale per metre, cut across like a geode. xyz: the distance to the
// facet's edge (x; y and z 1, so SL_Wire draws the facet edges), w: a random value per facet.
float4 SL_StaticFacets(float3 positionWS)
{
    float3 p = positionWS * max(_SL_CellScale, 0.01);
    float3 cell = floor(p);
    float3 f = frac(p);
    float d1 = 8.0, d2 = 8.0;
    float3 nearest = 0;
    for (int k = -1; k <= 1; k++)
    for (int j = -1; j <= 1; j++)
    for (int i = -1; i <= 1; i++)
    {
        float3 b = float3(i, j, k);
        float3 r = b - f + SL_Hash3(cell + b);
        float dd = dot(r, r);
        if (dd < d1) { d2 = d1; d1 = dd; nearest = cell + b; }
        else if (dd < d2) { d2 = dd; }
    }
    return float4((sqrt(d2) - sqrt(d1)) * 0.5, 1, 1, SL_Hash(nearest + 0.37));
}

// A structure's fragment starts here: the kit's clips, then the facets on the rest position (the
// base plus rest: its world position, but where a shard stood in the piece).
#define SL_FRAGMENT_BEGIN(i) \
    SL_StaticClip((i).positionCS, (i).positionWS, (i).uv, true); \
    (i).bary = SL_StaticFacets((i).rest + (i).center.xyz);

// The set's grade (StructureStyle.Gain and Peak) on a forward colour: a gain, then a soft limit on
// luminance keeping the hue, then a soft limit on the brightest channel under the bloom's knee
// (half the night's threshold of 1.5): the luminance limit alone lets a saturated hue's channel
// past it. So a self-lit structure blooms only as the beat raises the bloom's intensity, or when
// the effects light it, and never out-glows the horde or the effects.
float3 SL_GradeOf(float3 color, float peak) { return color / (1.0 + Luminance(color) / peak); }
#define SL_STATIC_CHANNEL_MAX 0.7
float3 SL_StaticGradeOf(float3 color, float peak)
{
    color = SL_GradeOf(color, peak);
    float m = max(color.r, max(color.g, color.b));
    float knee = SL_STATIC_CHANNEL_MAX * 0.5;
    if (m <= knee) return color;
    float limited = knee + (SL_STATIC_CHANNEL_MAX - knee) * (1.0 - exp(-(m - knee) / (SL_STATIC_CHANNEL_MAX - knee)));
    return color * (limited / m);
}
// A breaking shard's glow (_SL_PieceFx.w) eases the set's grade toward the horde's, without the
// channel limit, so a structure blooms at the moment it breaks and settles back.
#define SL_STATIC_BREAK_PEAK 2.5
#define SL_Grade(color) lerp(SL_StaticGradeOf((color) * _Gain, _Peak), \
    SL_GradeOf((color) * lerp(_Gain, 1.0, _SL_PieceFx.w), lerp(_Peak, SL_STATIC_BREAK_PEAK, _SL_PieceFx.w)), saturate(_SL_PieceFx.w))

#endif
