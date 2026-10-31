// What the crystal structures' shader shares with the style it came from (unity-game.md, "The
// Look"): demo3d's StyleCommon.hlsl under SL_STATIC, copied and owned for static meshes only. A
// structure's vertex front end and per-renderer values, the facets and the set's grade, on what
// every crystal shader shares (CrystalCommon.hlsl). The shader declares its UnityPerMaterial
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
#include "CrystalCommon.hlsl"

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
