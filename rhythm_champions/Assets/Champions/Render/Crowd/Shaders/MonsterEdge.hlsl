// The edge look's surface (design.md, The Look > The reference: the trailer; plan.md, Rendering > The edge look),
// shared by the monsters' bodies (MonsterEdge.shader) and shards (MonsterEdgeShard.shader); the frozen look
// package's MonsterEdge.hlsl, which drew the neon edge trailer, ported by review and unchanged: near-black glass whose
// only light is the type's palette hue, in a fresnel rim, in lines on the model's feature edges
// (StyleMesh.FeatureEdges, TEXCOORD5) and in a small inner core seen through the glass. The beat (the world's beat
// wave, MonsterCommon.hlsl) flares the core and lifts the edges, never the whole body; a hit flashes the light the
// body has toward white. Two touching monsters show a seam because each one's rim and outline are lit and the space
// between them is its dark core. The trailer's grade (Grade.cs: its void toe and hue-keeping filmic) then sinks the
// glass and the faint rim to black and keeps the lines' colour. --look trailer draws with exactly this (EdgeLookTests pins it); include after
// MonsterCommon.hlsl, in a shader whose UnityPerMaterial CBUFFER declares the properties below.
#ifndef CHAMPIONS_MONSTER_EDGE_INCLUDED
#define CHAMPIONS_MONSTER_EDGE_INCLUDED

#include "MonsterLines.hlsl"

// The CBUFFER's edge properties, set by EdgeLook from the palette (linear) and the look's gains:
//   half4 _Tint;     the type's hue, levelled (EdgeLook.Level)
//   half4 _Core;     void.body, the glass's colour
//   half4 _Flash;    a hit's and a break's colour (white)
//   half _RimPower;              the fresnel rim's falloff (its gain is the caller's `rim`)
//   half _Edge, _EdgePx, _Beat;  the feature lines' gain, width in pixels, and lift on the beat
//   half _CoreGlow, _CoreFlare, _CoreSize; the core's resting gain, its gain on the beat, its radius
//                                          as a share of the body's

// The glass, the rim (`rim` its gain) and the lines; `inside` 1 on a shard's fresh inside face, which
// has no rim (it faces into the break).
float3 EdgeSurface(float3 positionWS, float3 normalWS, float4 bary, float3 edges, float3 tint, float pulse, float inside,
                   float rim, out float lit, out float fres)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    float3 N = normalize(lerp(normalize(normalWS), facet, 0.6));
    float nv = saturate(dot(N, V));
    fres = pow(1 - nv, _RimPower) * (1 - inside);
    // The glass: each facet a shade apart (the triangle's random), so a flat face isn't one sheet.
    float3 color = _Core.rgb * (0.5 + bary.w);
    color += tint * rim * fres;
    lit = EdgeLine(bary.xyz, edges, _EdgePx);
    // Lines toward the silhouette a little brighter, so a body's outline leads its inner facets.
    color += tint * _Edge * lit * (0.65 + 0.35 * (1 - nv)) * (1 + _Beat * pulse);
    return color;
}

// The inner core: a glow round the body's centre (core.xyz, world; core.w its radius) where the view
// ray passes near it, so it sits inside the glass from every angle. Hot at its heart, flaring on the beat.
float3 EdgeCore(float3 positionWS, float4 core, float3 tint, float pulse)
{
    float3 ray = normalize(positionWS - _WorldSpaceCameraPos);
    float d = length(cross(core.xyz - _WorldSpaceCameraPos, ray));
    float r = max(core.w * _CoreSize, 1e-3);
    float glow = exp(-(d * d) / (r * r));
    float3 hot = lerp(tint, float3(1, 1, 1), 0.2 + 0.3 * pulse);
    return hot * glow * (_CoreGlow + _CoreFlare * pulse);
}

#endif
