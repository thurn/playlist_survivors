// The wireframe hologram monster look's surface (neon-look.md, The Axes > Monsters; neon-look-style.md,
// hologram), shared by its bodies (MonsterHologram.shader) and shards (MonsterHologramShard.shader):
// light over what is behind, which a faint veil dims (the shaders blend premultiplied). Lines in the
// type's palette hue on the model's feature edges (StyleMesh.FeatureEdges, TEXCOORD5) or every
// triangle's, a thin fresnel rim and a faint fill that thickens toward the silhouette, so a face
// turned to the camera is nearly clear and a body reads by its outline. A depth-only pass first keeps each draw to its front surfaces: a clump shows the near
// monster's lines over the far one's, never every back face's wire through it. The beat
// (_SL_Pulse) lifts the lines and sends a band up the body from its feet; a hit flashes the lines and
// rim toward shatter.core. Include after MonsterCommon.hlsl, in a shader whose UnityPerMaterial
// CBUFFER declares the properties below.
#ifndef LOOK_MONSTER_HOLOGRAM_INCLUDED
#define LOOK_MONSTER_HOLOGRAM_INCLUDED

#include "MonsterLines.hlsl"

#define ALL_EDGES_SHARE 0.4

// The CBUFFER's hologram properties, set by HologramLook from the palette (linear):
//   half4 _Tint;     the type's enemy slot (MonsterSlots), levelled (EdgeLook.Level)
//   half4 _Flash;    shatter.core, a hit's and a break's colour
//   half _AllEdges;  1: every triangle edge is a line (wireframe-all), 0: the feature edges only
//   half _Fill, _FillRim;        the fill's gain face-on and its extra toward the silhouette
//   half _Rim, _RimPower;        the fresnel rim's gain and falloff
//   half _Edge, _EdgePx, _Beat;  the lines' gain, width in pixels, and lift on the beat
//   half _Sweep, _SweepWidth;    the beat's band: its gain and half-width as a share of the body's height

// The fill, the rim and the lines at this pixel (linear, to be added); `inside` 1 on a shard's fresh
// inside face, which has neither rim nor fill (it faces into the break). `lit` the line's coverage,
// `fres` the rim's, for the caller's flashes and rings.
float3 HologramSurface(float3 positionWS, float3 normalWS, float4 bary, float3 edges, float3 tint, float pulse, float inside,
                       out float lit, out float fres)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    float3 N = normalize(lerp(normalize(normalWS), facet, 0.5));
    float nv = saturate(dot(N, V));
    float edge = 1 - nv;
    fres = pow(edge, _RimPower) * (1 - inside);
    // The fill: faint face-on, thicker toward the silhouette, each facet a little apart (the triangle's
    // random) so a flat face isn't one sheet.
    float3 color = tint * (_Fill * (0.8 + 0.4 * bary.w) + _FillRim * edge * edge) * (1 - 0.7 * inside);
    color += tint * _Rim * fres;
    // Under wireframe-all the triangulation's edges are lines too, at a share of the feature edges' light,
    // so the model's facets still lead.
    lit = EdgeLine(bary.xyz, max(edges, ALL_EDGES_SHARE * _AllEdges), _EdgePx);
    color += tint * _Edge * lit * (0.75 + 0.25 * edge) * (1 + _Beat * pulse);
    return color;
}

// The beat's band: at the body's feet on the beat's frame, rising to its head as the pulse decays and
// fading with it. h01 the pixel's height over the body (0 feet, 1 head).
float HologramSweep(float h01, float pulse)
{
    float x = (h01 - (1 - pulse)) / max(_SweepWidth, 1e-3);
    return _Sweep * pulse * exp(-x * x);
}

#endif
