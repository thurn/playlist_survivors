// The hero looks' front end (neon-look.md, The Axes > Hero; HeroLook.cs): what the four hero shaders
// (Resources/Look/Shaders/HeroEdge, HeroSolid, HeroHologram, HeroGem) share. Each draws both the
// knight, from the hero bake's faceted copies of its meshes (HeroFacets: barycentrics plus a random
// value per triangle in TEXCOORD3, feature edges in TEXCOORD5, skinned by Unity), and the crystal
// blade (HeroBlade's mesh: the spine and the length in TEXCOORD1, barycentrics in TEXCOORD3, every
// edge a line through _AllEdges). The hero sits on the style guide's hero light rung, above every
// monster: its light is hero.body, near white, and the blade's spine and blow flare reach white.
// The shader declares its UnityPerMaterial CBUFFER, with the properties below, before including this.
#ifndef PS_LOOK_HERO_INCLUDED
#define PS_LOOK_HERO_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Shaders/StaticShadow.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Shaders/CrystalCommon.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterLines.hlsl"
#include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"

// The CBUFFER's shared properties, set by HeroLook from the palette (linear):
//   half4 _Body;           hero.body, the hero's light
//   half4 _Accent;         hero.trail, a faint second colour (the gem's fringe, the hologram's fill)
//   half4 _EmissionColor;  added light: the game's low-health pulse (HeroPresenter's property block)
//   half _Blade;           1 on the blade's material, 0 on the knight's
//   half _AllEdges;        1: every triangle edge is a line (the blade), 0: the feature edges only
//   half _EdgePx;          the lines' width in pixels
//   half _Glow, _BladeFlare; the blade's spine light and its blow flare (HeroBlade sets the flare)

// 1 while the stills draw their ID render (Stills.cs, demo3d's DemoMain.MaskMode): the hero draws
// black, so the ID render marks only the crowd and the hero hides what it stands in front of.
float _GameIdMask;

// The post's toe and exposure (PostStack.PublishToe): w the scene's own exposure's factor, 0 with no look post.
float4 _LookToe;
// The scene exposure, in stops, the gains were tuned under: game's baked arena's (arena-layout.json's lighting
// exposure). The hero takes out the scene's own exposure and puts this back, so it lands on the same rung in game and
// in demo3d's crystal cut, which sets none.
#define HERO_TUNED_EXPOSURE 0.8

float HeroLevel() { return _LookToe.w > 0 ? exp2(HERO_TUNED_EXPOSURE) / _LookToe.w : 1; }

struct HeroAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 core : TEXCOORD1;   // the blade's: x 1 on the spine, 0 on the edges; y 0 at the guard, 1 at the tip (0 on the knight)
    float4 bary : TEXCOORD3;   // barycentrics and a random value per triangle
    float3 edges : TEXCOORD5;  // the knight's feature edges (component k: the edge opposite corner k); 0 on the blade
};

struct HeroVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float2 core : TEXCOORD2;
    float4 bary : TEXCOORD3;
    nointerpolation float3 edges : TEXCOORD4;
    float fogFactor : TEXCOORD5;
};

HeroVaryings HeroVert(HeroAttributes v)
{
    HeroVaryings o;
    o.positionWS = TransformObjectToWorld(v.positionOS);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    o.core = v.core;
    o.bary = v.bary;
    o.edges = max(v.edges, _AllEdges);
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    return o;
}

// The facet's normal leaned toward the vertex normal by (1 - facets), facing the camera, and how much
// it faces it (nv).
float3 HeroNormal(float3 positionWS, float3 normalWS, float facets, out float nv)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    float3 N = normalize(lerp(normalize(normalWS), facet, facets));
    nv = saturate(dot(N, V));
    return N;
}

// The beat on the hero: the beat wave leaving its feet (SL_Wave), quick, so the hero breathes on
// every beat without strobing (the restraint: a lift, never a flash).
float HeroBeat(float3 positionWS) { return SL_Wave(positionWS, 7.0); }

// The pixel's height over the hero's feet as a share of its height (0 feet, 1 head).
float HeroHeight01(float3 positionWS) { return saturate((positionWS.y - _SL_HeroPos.y) / 1.85); }

// The blade's own light, the same in every look: a line down the spine fading toward the tip, up on
// each beat, and the blow flare over the whole blade past white (the flare the blade has always had,
// earned white: it rides the blow's frame). 0 on the knight.
float3 HeroBladeLight(float2 core, float beat)
{
    float spine = smoothstep(0.55, 1.0, core.x);
    float glow = _Glow * spine * (1.0 - 0.4 * core.y) * (0.75 + 0.5 * beat) + _BladeFlare * (0.35 + 0.65 * spine);
    return _Body.rgb * 2.0 * glow * _Blade;
}

// What every look adds last: the blade's light, the low-health pulse, and fog. `veil` is the alpha of a
// look blended premultiplied (the hologram: light added over what is behind, dimmed by the veil), whose
// fog fades its light rather than mixing in the fog's colour; 1 for an opaque look. Black under the ID render.
half4 HeroFinish(HeroVaryings i, float3 color, float beat, float veil)
{
    if (_GameIdMask > 0.5) return half4(0, 0, 0, 1);
    color += HeroBladeLight(i.core, beat);
    color *= HeroLevel();
    color += _EmissionColor.rgb;
    color = veil < 1 ? MixFogColor(color, 0, i.fogFactor) : MixFog(color, i.fogFactor);
    return half4(color, veil);
}

#endif
