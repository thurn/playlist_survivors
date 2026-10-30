// The fire's flames treatment's material inputs, displacement and death (FireShell.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by FireShell.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_FIRESHELL_INPUT_INCLUDED
#define DEMO3D_STYLE_FIRESHELL_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    half4 _Ember; half4 _Flame; half4 _Core; float _Thickness; float _Lick; half _Intensity;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

#include "StyleCommon.hlsl"
#include "FireCommon.hlsl"

#endif
