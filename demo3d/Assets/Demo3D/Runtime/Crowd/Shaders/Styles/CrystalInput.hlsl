// The crystal treatment's material inputs, displacement and death (Crystal.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Crystal.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_CRYSTAL_INPUT_INCLUDED
#define DEMO3D_STYLE_CRYSTAL_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    half4 _Tint; float _Refraction; half _Facets;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

#define SL_STYLE_MESH
#define SL_DISSOLVE(rest, death) 0.0
#define SL_DISPLACE(v, m, slot, positionWS, normalWS) SL_ShatterDisplace(v, m, slot, positionWS, 1.6);
#include "StyleCommon.hlsl"

#endif
