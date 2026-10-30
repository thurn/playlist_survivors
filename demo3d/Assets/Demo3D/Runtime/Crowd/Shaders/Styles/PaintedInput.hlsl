// The painted treatment's material inputs, displacement and death (Painted.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Painted.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_PAINTED_INPUT_INCLUDED
#define DEMO3D_STYLE_PAINTED_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST; half4 _BaseColor;
    float4 _LumRange; half4 _ShadowColor; half4 _RimColor; half4 _SpecColor; half _Gloss;
    half _Sheen; half4 _SpotColor; half _SpotScale; half _SpotAmount; half _Ambient;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

#include "StyleCommon.hlsl"

#endif
