// The spectrum treatment's material inputs, displacement and death (Spectrum.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Spectrum.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_SPECTRUM_INPUT_INCLUDED
#define DEMO3D_STYLE_SPECTRUM_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    half _Hue; half _WireGlow; float _Bulge;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

// The bass swell along the normal, a ripple up the body; then the shards.
#define SL_STYLE_MESH
#define SL_DISSOLVE(rest, death) 0.0
#define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
    positionWS += normalWS * _Bulge * _SL_Pulse * (0.6 + 0.4 * sin(saturate(v.positionOS.y / max(_HeightM, 0.1)) * 12.0 - _SL_Time * 10.0)); \
    SL_ShatterDisplace(v, m, slot, positionWS, 1.3);
#include "StyleCommon.hlsl"

#endif
