// The refractive-gem monster look's surface (neon-look.md, The Axes > Monsters; neon-look-style.md,
// prism), shared by its bodies (MonsterGem.shader) and shards (MonsterGemShard.shader): clear gem
// that shows the void and the grid behind it (the camera's opaque texture) bent by each facet and
// split into the type's palette hue and its two neighbours (dispersion in the slot's family, not a
// rainbow), so a body is mostly dark with bright facet highlights: hard bands of reflected light
// that jump between facets as the view turns, a fresnel rim in the slot's hue and fine pale lines
// on the model's feature edges (MonsterLines.hlsl's EdgeLine). A few facets hold trapped light that
// sparkles on the beat (_SL_Pulse); the core is MonsterEdge.hlsl's EdgeCore. Two touching monsters
// show a seam because each one's rim and outline are lit and the void seen through them is dark.
// Include after MonsterCommon.hlsl and MonsterEdge.hlsl, in a shader whose UnityPerMaterial
// CBUFFER declares MonsterEdge.hlsl's properties and the ones below.
#ifndef LOOK_MONSTER_GEM_INCLUDED
#define LOOK_MONSTER_GEM_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

// The CBUFFER's gem properties, set by GemLook from the palette (linear):
//   half4 _FringeA, _FringeB;  the slot's hue turned toward its neighbours: the dispersion's fringes
//   float _Refraction;         how far a facet bends the scene behind (screen share per unit of normal)
//   half _Behind;              the refracted scene's gain
//   half _Shine, _ShineWidth;  the facets' reflected bands: gain, and the share of facets that catch one
//   half _Sparkle;             a trapped-light facet's lift on the beat
//   half _Far;                 the glow a far body keeps (see GemFar)

// How far a body is, from its bounding sphere's centre (CrowdSphere) and its standing height (m):
// 1 while it stands under 7% of the screen's half-height (38 px at 1080p), 0 over 14%. A body that
// small (most of the crystal cut's horde; the game's stand 60 px and up) averages its narrow
// bands and fine lines away to a dim outline, so it keeps a glow in its slot's hue; at the game's size
// it has none. In screen shares, so supersampling doesn't move it.
float GemFar(float4 sphere, float height)
{
    float4 c = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
    float share = height * abs(UNITY_MATRIX_P[1][1]) / max(c.w, 1e-3);
    return 1 - saturate((share - 0.07) / 0.07);
}

// The gem; `inside` 1 on a shard's fresh inside face, which has no rim (it faces into the break);
// `far` GemFar's. lit: the feature lines' light (0..1); shine: the reflected bands and the rim, the
// light a ring or a hit lifts.
float3 GemSurface(float3 positionWS, float3 normalWS, float4 positionCS, float4 bary, float3 edges, float3 tint,
                  float pulse, float inside, float far, out float lit, out float shine)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
    // A cut stone: the facets lead, so neighbouring faces bend and reflect apart.
    float3 N = normalize(lerp(normalize(normalWS), facet, 0.85));
    float nv = saturate(dot(N, V));
    float fres = pow(1 - nv, _RimPower) * (1 - inside);
    float faceRand = bary.w;

    // The scene behind, bent by the facet: three taps along the bend, each in one hue of the slot's
    // family, keeping some of what is behind's own colour (a zone's grid stays its tint).
    float2 suv = GetNormalizedScreenSpaceUV(positionCS);
    float2 off = mul((float3x3)UNITY_MATRIX_V, N).xy * _Refraction;
    float3 a = SampleSceneColor(suv + off * 0.7);
    float3 b = SampleSceneColor(suv + off * 1.0);
    float3 c = SampleSceneColor(suv + off * 1.3);
    float3 split = (Luminance(a) * _FringeA.rgb + Luminance(b) * tint + Luminance(c) * _FringeB.rgb)
                 / max(Luminance(_FringeA.rgb + tint + _FringeB.rgb), 1e-3);
    float3 color = _Core.rgb * (0.5 + faceRand) + lerp(b, split, 0.7) * _Behind;

    // Light trapped in the stone: a few facets hold it, and the beat makes them sparkle, never the whole body.
    float trapped = smoothstep(0.8, 1.0, faceRand);
    color += tint * trapped * (0.1 + _Sparkle * pulse) * (1 + 1.5 * inside);
    color += tint * _Far * far * (0.4 + 0.6 * faceRand);

    // Reflected bands of an unseen sky: a narrow band per facet, so a turn of the body or the camera
    // moves the highlights from facet to facet; pale (the hue toward white), brightest at grazing views.
    float3 pale = lerp(tint, Luminance(tint).xxx * 1.6, 0.45);
    float3 R = reflect(-V, N);
    float x = frac(dot(R, float3(1.7, 2.3, 1.1)) + faceRand * 0.35);
    float band = smoothstep(0, 0.03, x) * (1 - smoothstep(_ShineWidth, _ShineWidth + 0.03, x));
    float bands = band * (0.35 + 0.65 * (1 - nv));
    color += pale * _Shine * bands;
    color += tint * _Rim * fres;

    lit = EdgeLine(bary.xyz, edges, _EdgePx);
    color += pale * _Edge * lit * (1 + _Beat * pulse);
    shine = bands + fres;
    return color;
}

// A hit's and a break's light: half shatter.core, half the slot's hue (a white flash washed a crowd
// under constant hits to white, as it washed the crystal's amber flowers to peach, ps-msz.8.3).
float3 GemFlash(float3 tint) { return lerp(_Flash.rgb, tint * 2, 0.5); }

#endif
