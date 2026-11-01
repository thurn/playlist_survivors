// The arena's static shadow map (unity-game.md, "The Arena"; demo3d's environment.md, Lighting the
// night): the static set's depth, rendered at load from the moon's direction over the whole arena
// and again after a structure breaks or regrows (StaticShadowMap.cs, ArenaRuntime.RefreshShadows).
// The kit (lit by probes, LitStaticShadow), the crystal structures and, later, the crowd and hero
// sample it, so everything walks through tree shade. The kit's own shadows are out of the realtime
// cascades; the structures, outside the bake, still cast there. Include after URP's Lighting.hlsl
// (for sampler_LinearClampCompare and the PBR helpers).
#ifndef GAME_STATIC_SHADOW_INCLUDED
#define GAME_STATIC_SHADOW_INCLUDED

TEXTURE2D_SHADOW(_GameStaticShadowMap);
float4x4 _GameStaticShadowMatrix;   // world to shadow texture (xy in 0..1, z the compare depth)
float4 _GameStaticShadowParams;     // x on (0 or 1), y one texel in uv, z strength (0..1)

// 1 where the moon reaches the point past the static set, 0 in full static shade. Outside the
// map (or with the map off) it is 1. Four bilinear compare taps half a texel apart: a 2x2 tent.
half GameStaticShadow(float3 positionWS)
{
    if (_GameStaticShadowParams.x < 0.5) return 1;
    float4 c = mul(_GameStaticShadowMatrix, float4(positionWS, 1));
    if (c.x <= 0 || c.y <= 0 || c.x >= 1 || c.y >= 1) return 1;
    float t = _GameStaticShadowParams.y * 0.5;
    half s = SAMPLE_TEXTURE2D_SHADOW(_GameStaticShadowMap, sampler_LinearClampCompare, float3(c.x - t, c.y - t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_GameStaticShadowMap, sampler_LinearClampCompare, float3(c.x + t, c.y - t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_GameStaticShadowMap, sampler_LinearClampCompare, float3(c.x - t, c.y + t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_GameStaticShadowMap, sampler_LinearClampCompare, float3(c.x + t, c.y + t, c.z));
    return lerp(1, s * 0.25, _GameStaticShadowParams.z);
}

// The main light's scale: 1, or a shader's own before the include (the crowd's key light, CrowdSkinned).
#ifndef GAME_MAIN_LIGHT_SCALE
#define GAME_MAIN_LIGHT_SCALE 1
#endif

// URP's UniversalFragmentPBR without clear coat, light layers or debug views, with the main
// light's realtime shadow combined with the static shadow map. The cascades hold only what moves
// and the structures, which the map also holds: min() keeps a point shaded by both from going
// darker than either.
half3 GameLightingPBR(InputData inputData, SurfaceData surfaceData)
{
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, GameStaticShadow(inputData.positionWS));
    mainLight.color *= GAME_MAIN_LIGHT_SCALE;
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(brdfData, brdfData, 0, inputData.bakedGI,
        aoFactor.indirectAmbientOcclusion, inputData.positionWS, inputData.normalWS,
        inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfData, mainLight,
        inputData.normalWS, inputData.viewDirectionWS, 0, false);
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfData, light,
            inputData.normalWS, inputData.viewDirectionWS, 0, false);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfData, light,
            inputData.normalWS, inputData.viewDirectionWS, 0, false);
    LIGHT_LOOP_END
    #endif
    return CalculateFinalColor(lightingData, 1).rgb;
}

#endif
