// The fire treatment's colour ramp (stylelab's FireCommon.hlsl): black, then ember, flame and a
// white-hot core.
#ifndef DEMO3D_STYLE_FIRE_INCLUDED
#define DEMO3D_STYLE_FIRE_INCLUDED

float3 FireRamp(float t, float3 ember, float3 flame, float3 core)
{
    t = saturate(t);
    float3 c = lerp(float3(0, 0, 0), ember, smoothstep(0.0, 0.3, t));
    c = lerp(c, flame, smoothstep(0.25, 0.6, t));
    c = lerp(c, core, smoothstep(0.55, 1.0, t));
    return c;
}

#endif
