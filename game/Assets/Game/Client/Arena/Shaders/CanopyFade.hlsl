// The canopy fade (demo3d's TD15; docs/tech-demo-3d/environment.md, Environment rendering): tree pixels between the camera and
// the hero, within a few metres of it on screen, dither out, so a forest edge never hides the hero.
// CanopyFade.cs publishes the hero; the canopy materials (Game/LitCanopy, and LitStaticShadow's
// forward pass with _CANOPY_FADE) call GameCanopyClip in every pass that writes depth or colour.
// Include after URP's Core.hlsl.
#ifndef GAME_CANOPY_FADE_INCLUDED
#define GAME_CANOPY_FADE_INCLUDED

float4 _GameHero;        // xyz: the hero's chest in world space; w: the fade's radius in metres at the hero's depth (0: off)

// 0 outside the fade, 1 where the pixel is fully cleared: on the hero's side of the camera, nearer
// than the hero by at least half a metre, and within the radius of it on screen (measured at the
// hero's depth, so the hole keeps its size in metres whatever the view width), with a soft rim.
float GameCanopyFade(float3 positionWS)
{
    if (_GameHero.w <= 0) return 0;
    float3 hv = TransformWorldToView(_GameHero.xyz);
    float3 pv = TransformWorldToView(positionWS);
    float hd = -hv.z, pd = -pv.z;               // view space looks down -z
    if (pd <= 0 || pd > hd - 0.5) return 0;    // behind or beside the hero: it never hides it
    float2 atHero = pv.xy * (hd / pd);
    float d = length(atHero - hv.xy);
    return saturate((_GameHero.w - d) / (0.4 * _GameHero.w));
}

// Discards by a 4x4 ordered dither: none at fade 0, every pixel at fade 1.
void GameCanopyClip(float4 positionCS, float3 positionWS)
{
    float fade = GameCanopyFade(positionWS);
    if (fade <= 0) return;
    const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    uint2 p = (uint2)positionCS.xy & 3;
    float threshold = (bayer[p.y * 4 + p.x] + 0.5) / 16.0;
    clip(threshold - fade);
}

#endif
