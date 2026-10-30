// The hologram treatment's material inputs, displacement and death (Hologram.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Hologram.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_HOLOGRAM_INPUT_INCLUDED
#define DEMO3D_STYLE_HOLOGRAM_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    half4 _Color; float _Glitch;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

// The scan-out: everything above a line falling from the head is cut.
#define SL_STYLE_MESH
#define SL_DISSOLVE(rest, death) HologramScanOut(rest, death)
// Glitch slices: just after each off-beat (and while dying), a few horizontal slices jump sideways.
#define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
    { \
        float active = step(frac(_SL_Beat + 0.5), 0.14) + m.dissolve; \
        float r = SL_Hash1(floor(v.positionOS.y * 7.0) + floor(_SL_Time * 18.0) + SL_SeedOf(slot) * 31.0); \
        float3 side = normalize(cross(float3(0, 1, 0), GetWorldSpaceNormalizeViewDir(positionWS)) + 1e-4); \
        positionWS += side * step(0.75, r) * (r - 0.75) * 4.0 * _Glitch * saturate(active); \
    }
#include "StyleCommon.hlsl"

// Clips above the scan line; returns its bright edge.
float HologramScanOut(float3 rest, float death)
{
    if (death <= 0.0) return 0.0;
    float h01 = saturate(rest.y / max(_HeightM, 0.1));
    float cut = 1.0 - death * 1.1;
    clip(cut - h01);
    return smoothstep(0.06, 0.0, cut - h01);
}

#endif
