// The chrome treatment's material inputs, displacement and death (Chrome.shader's HLSLINCLUDE): its
// UnityPerMaterial CBUFFER, then StyleCommon.hlsl. Shared by Chrome.shader (the crowd) and its structure
// twin (Arena/Shaders/Structures/, which defines SL_STATIC first; environment.md, Structure styles).
#ifndef DEMO3D_STYLE_CHROME_INPUT_INCLUDED
#define DEMO3D_STYLE_CHROME_INPUT_INCLUDED

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST; half4 _BaseColor;
    half4 _Tint; float _Ripple;
    half _Gain; half _Peak;
    float _HeightM;
CBUFFER_END

// The ripple, then the melt: everything sinks to a puddle that spreads out round the feet.
#define SL_DISPLACE(v, m, slot, positionWS, normalWS) \
{ \
    positionWS += normalWS * _Ripple * sin((v).positionOS.y * 16.0 - _SL_Time * 7.0 + SL_SeedOf(slot) * 6.28) * (0.4 + 0.8 * _SL_Pulse); \
    float melt = smoothstep(0.0, 1.0, (m).dissolve); \
    float groundY = (m).position.y + 0.02; \
    positionWS.y = lerp(positionWS.y, groundY + (positionWS.y - groundY) * 0.04, melt); \
    positionWS.xz += (positionWS.xz - (m).position.xz) * melt * 0.9; \
}
// The puddle lingers, then the baseline's dissolve takes it in the last third of the death.
#define SL_DISSOLVE(rest, death) SL_Dissolve(rest, saturate((death) * 3.0 - 2.0))
#include "StyleCommon.hlsl"

#endif
