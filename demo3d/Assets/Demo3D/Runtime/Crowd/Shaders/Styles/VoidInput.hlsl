// The void treatment's material inputs, displacement and death (Void.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Void.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_VOID_INPUT_INCLUDED
#define DEMO3D_STYLE_VOID_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST; half4 _BaseColor;
    half4 _RimColor; half _RimPower; half4 _NebulaA; half4 _NebulaB;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

// _HeightM is at instance scale 1; the instance's scale is in its basis (CrowdSphere's height).
#define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
{ \
    float implode = smoothstep(0.0, 1.0, (m).dissolve); \
    float3 middle = (m).position + float3(0, _HeightM * 0.5 * (m).scale, 0); \
    positionWS = lerp(positionWS, middle, implode * implode); \
}
#define SL_DISSOLVE(rest, death) 0.0
#include "StyleCommon.hlsl"

#endif
