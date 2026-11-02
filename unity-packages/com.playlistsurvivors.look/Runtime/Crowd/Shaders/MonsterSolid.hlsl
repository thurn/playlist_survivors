// The solid-emissive monster look's surface (neon-look.md, The Axes > Monsters; neon-look-style.md,
// solid), shared by its bodies (MonsterSolid.shader) and shards (MonsterSolidShard.shader): a
// saturated self-lit body in the type's palette hue, shaded so it shows form (a soft light from
// above the camera, each facet a step apart, and a darker falloff toward the silhouette), and a thin
// dark outline in screen space drawn as an inverted hull (MonsterOutline.hlsl), so two touching
// monsters of one type show a dark seam between them. The beat (_SL_Pulse, the beat globals) lights
// a small hot spot where the body faces the camera, never the whole body; a passing impulse ring
// (ImpulseCrowd) lifts the body and lights it in the ring's colour; a hit lightens it toward
// shatter.core. Include after MonsterCommon.hlsl, in a shader whose UnityPerMaterial CBUFFER
// declares the properties below.
#ifndef LOOK_MONSTER_SOLID_INCLUDED
#define LOOK_MONSTER_SOLID_INCLUDED

#include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"
#include "MonsterOutline.hlsl"

// The CBUFFER's solid properties, set by SolidLook from the palette (linear):
//   half4 _Tint;     the type's enemy slot (MonsterSlots)
//   half4 _Outline;  void.floor, the outline's colour
//   half4 _Flash;    shatter.core, a hit's and a break's colour
//   half _Glow;                  the body's emission gain (1: the slot's colour where fully lit)
//   half _Key;                   how much of the body's light comes from the soft light above
//   half _Falloff, _FalloffPx;   how dark the silhouette band gets (1: black), and its width in pixels at 1080 rows
//   half _Facets;                how far the shading follows each facet rather than the smooth normal
//   half _HotSpot;               the hot spot's gain on the beat
//   half _Impulse;               how brightly a passing impulse ring lights the body
//   half _OutlinePx;             the outline's width in pixels at 1080 rows

// The body's light: `inside` 1 on a shard's fresh inside face, which has no silhouette band (it
// faces into the break) and is a little brighter, as a broken gem's fresh face is; `radiusPx` the
// body's radius on screen (SolidRadiusPx).
float3 SolidSurface(float3 positionWS, float3 normalWS, float4 bary, float3 tint, float pulse, float inside, float radiusPx)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    float3 N = normalize(lerp(normalize(normalWS), facet, _Facets));
    float nv = saturate(dot(N, V));
    // A soft light from above, leaning toward the camera so the faces it sees are the lit ones.
    float3 L = normalize(float3(0, 1, 0) + 0.5 * V);
    float key = 0.5 + 0.5 * dot(N, L);
    float shade = (1 - _Key) + _Key * key;
    // A dark band _FalloffPx wide just inside the silhouette, so a body's own edge is dark and the
    // post's void toe sinks it to black: the seam two touching (or interpenetrating, which hides the
    // outline hull) neighbours show. On a body r pixels across a band d pixels in from the edge is
    // where nv < sqrt(2d / r), so the band keeps its width at any size; the cap keeps a small body's
    // sides lit, which face the trailer's steep camera at nv near 0.5, and the outline separates it.
    float edge = min(0.4, sqrt(2 * _FalloffPx / max(radiusPx, 1)));
    float band = 1 - _Falloff * (1 - smoothstep(0.6 * edge, edge, nv));
    // A body large on screen (from 50 px in radius, fully from 100) also darkens broadly toward its
    // silhouette: the game's close bodies (85 to 150 px) crowd and interpenetrate, and a thin band
    // alone leaves a pack of them one bright mass. A trailer body (35 to 60 px) keeps its sides lit.
    float broad = 1 - _Falloff * saturate((radiusPx - 50) / 50) * (1 - pow(nv, 1.5));
    float fall = lerp(min(band, broad), 1, inside);
    float3 color = tint * _Glow * shade * fall * (0.9 + 0.2 * bary.w) * (1 + 0.35 * inside);
    // The beat's hot spot: the faces turned to the camera flare toward white.
    color += lerp(tint, 1, 0.35) * _HotSpot * pulse * pow(nv, 8);
    return color;
}

// A body's radius on screen in pixels at 1080 rows, from its bounding sphere (CrowdSphere): its
// centre and the point a radius above it along the camera's up axis, projected.
float SolidRadiusPx(float4 sphere)
{
    float4 c = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
    float4 t = mul(UNITY_MATRIX_VP, float4(sphere.xyz + sphere.w * UNITY_MATRIX_I_V._m01_m11_m21, 1));
    return abs(t.y / max(t.w, 1e-3) - c.y / max(c.w, 1e-3)) * 0.5 * 1080.0;
}

// A hit (the crowd's hit flash, 1 on the blow's frame and fading): the faces turned to the camera
// brighten in the body's own hue and the most frontal lean toward shatter.core, while the
// silhouette band stays dark, so a pack under an area weapon flares but stays coloured and keeps
// its form and seams.
float3 SolidHit(float3 color, float3 positionWS, float3 normalWS, float hit)
{
    float nv = saturate(dot(normalize(normalWS), GetWorldSpaceNormalizeViewDir(positionWS)));
    return color * (1 + 0.4 * hit * smoothstep(0.3, 0.7, nv)) + _Flash.rgb * (0.2 * hit * pow(nv, 4));
}

// A passing impulse ring on a monster standing at `root`: its body lifts by the ring's hop, and the
// light it adds comes back for the fragment, in the body's own hue with a touch of the rings'
// colour, softly capped (rings stack: a mass kill's merged rings sum far past white).
float3 SolidImpulse(float3 root, inout float3 positionWS)
{
    float hop;
    float3 light;
    ImpulseCrowd(root.xz, hop, light);
    positionWS.y += hop;
    float l = Luminance(light);
    return (_Tint.rgb + 0.25 * light / max(l, 1e-4)) * (_Impulse * l / (1 + l));
}

#endif
