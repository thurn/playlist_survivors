// The crystal look's surface (crowd.md, Monster styles), shared by Crystal.shader (the crowd) and
// CrystalShard.shader (its shards, CrystalShatter): faceted gemstone that refracts the camera's
// opaque texture with a little dispersion, tinted by the type's gem colour, with light trapped in
// each facet, hard banded reflections, facet edges that catch the light and glints as the beat wave
// passes. Include it in a forward pass after StyleCommon.hlsl (the SubShader's HLSLINCLUDE, with
// Crystal's UnityPerMaterial CBUFFER). Under SL_STATIC (crystal structures) bary is SL_StaticFacets'.
#ifndef DEMO3D_CRYSTAL_COMMON_INCLUDED
#define DEMO3D_CRYSTAL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

// One crystal fragment. bary: the style mesh's barycentrics (xyz) and value per triangle (w);
// hit: the hit flash (0..1); glow: a gem-coloured glow (the crowd's flying triangles, 0..1);
// inside: 1 on a shard's inside faces, which are brighter and more saturated than the skin, as a
// broken gem's fresh faces are.
half4 CrystalShade(float3 positionWS, float3 normalWS, float4 positionCS, float4 bary, float hit, float glow,
                   float inside, float fogFactor)
{
    float3 V = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 facet = normalize(cross(ddy(positionWS), ddx(positionWS)));
    if (dot(facet, V) < 0) facet = -facet;
#if defined(SL_STATIC)
    // A structure's facets are SL_StaticFacets' cells across the kit's few large triangles: each
    // cell is tilted its own way, as a cut stone's faces are.
    facet = normalize(facet + (SL_Hash3(bary.w * 91.3 + 3.1) - 0.5) * 1.2);
    if (dot(facet, V) < 0) facet = normalize(facet - 1.2 * dot(facet, V) * V);
#endif
    float3 N = normalize(lerp(normalize(normalWS), facet, _Facets));
    float NdotV = saturate(dot(N, V));

    float2 suv = GetNormalizedScreenSpaceUV(positionCS);
    float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
    float2 off = nv.xy * _Refraction;
    float3 behind;
    behind.r = SampleSceneColor(suv + off * 1.0).r;
    behind.g = SampleSceneColor(suv + off * 1.2).g;
    behind.b = SampleSceneColor(suv + off * 1.4).b;

    float3 tint = _Tint.rgb;
    float wave = SL_Wave(positionWS, 6.0);
    float faceRand = bary.w;
#if defined(SL_STATIC)
    behind *= 0.5; // a structure is thicker, darker gem: it doesn't uncover the lighter ground behind the horde beside it
#endif
#if defined(CRYSTAL_BEHIND)
    behind *= CRYSTAL_BEHIND; // the hero's blade (CrystalBlade.shader): a denser white gem, lit from inside
#endif
    float3 color = behind * lerp(float3(1, 1, 1), tint, lerp(0.85, 1.0, inside)) * 1.2 + tint * lerp(0.08, 0.45, inside);
    // Light trapped inside: each facet its own brightness, like a cut stone, and a core
    // that throbs on the beat.
    color += tint * (0.1 + 0.85 * faceRand * faceRand) * (0.55 + 1.1 * wave) * (1.0 + 1.5 * inside);
    color += tint * pow(NdotV, 3.0) * 0.35 * (1.0 + wave);

    Light L = SL_MainLight(positionWS);
    float3 R = reflect(-V, N);
    float fres = pow(1.0 - NdotV, 3.0);
    // A cut stone's reflections: hard bands, so neighbouring facets flip between bright and dark.
    float band = smoothstep(0.42, 0.58, frac(dot(R, float3(1.7, 2.3, 1.1)) + faceRand * 0.35));
    float3 env = lerp(float3(0.02, 0.02, 0.05), float3(0.65, 0.7, 1.0), band) * lerp(float3(1, 1, 1), tint, 0.35);
    color += env * (0.3 + fres * 1.3) + tint * fres * 0.7;
    float glint = pow(saturate(dot(R, L.direction)), 300.0) * 25.0 * L.shadowAttenuation;
    color += glint * L.color;
    float3 addDiffuse = 0, addSpec = 0;
    SL_AdditionalLights(positionWS, positionCS, N, V, 0.0, 250.0, addDiffuse, addSpec);
    color += addSpec * 6.0 + addDiffuse * tint * 0.15;

    // Facet edges catch the light; a few facets flash as each wave passes.
    color += SL_Wire(bary.xyz, 1.0) * (tint * 0.25 + 0.1) * (0.4 + wave);
    float twinkle = step(0.88, SL_Hash1(faceRand * 91.7 + SL_WaveBeat(positionWS) * 13.1));
    color += twinkle * wave * (tint * 3.0 + 1.5);
    color += float3(1.0, 1.0, 1.1) * hit + tint * 3.0 * glow;
    // A fresh break: more saturated than the weathered skin.
    if (inside > 0) color = max(0, lerp(SL_Luma(color).xxx, color, 1.0 + 0.6 * inside));
    return half4(MixFog(SL_Grade(color), fogFactor), 1);
}

#endif
