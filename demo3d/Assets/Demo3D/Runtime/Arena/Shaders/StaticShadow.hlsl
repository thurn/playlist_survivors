// The arena's static shadow map (environment.md, Lighting the night): the static set's depth,
// rendered once at load from the moon's direction over the whole arena (StaticShadowMap.cs).
// The crowd and hero shaders sample it, so monsters walk through tree shade while the realtime
// cascades hold only the monsters and the hero (rung E2). Include after URP's Lighting.hlsl
// (for sampler_LinearClampCompare and the PBR helpers).
#ifndef DEMO3D_STATIC_SHADOW_INCLUDED
#define DEMO3D_STATIC_SHADOW_INCLUDED

TEXTURE2D_SHADOW(_Demo3DStaticShadowMap);
float4x4 _Demo3DStaticShadowMatrix;   // world to shadow texture (xy in 0..1, z the compare depth)
float4 _Demo3DStaticShadowParams;     // x on (0 or 1), y one texel in uv, z strength (0..1)
// TD15's ID render for the readability measure: 1 draws the crowd flat white (CrowdSkinned) and the
// set and hero black with their depth (LitStaticShadow), so the set occludes the monsters behind it.
float _Demo3DIdMask;

// 1 where the moon reaches the point past the static set, 0 in full static shade. Outside the
// map (or with the map off) it is 1. Four bilinear compare taps half a texel apart: a 2x2 tent.
half Demo3DStaticShadow(float3 positionWS)
{
    if (_Demo3DStaticShadowParams.x < 0.5) return 1;
    float4 c = mul(_Demo3DStaticShadowMatrix, float4(positionWS, 1));
    if (c.x <= 0 || c.y <= 0 || c.x >= 1 || c.y >= 1) return 1;
    float t = _Demo3DStaticShadowParams.y * 0.5;
    half s = SAMPLE_TEXTURE2D_SHADOW(_Demo3DStaticShadowMap, sampler_LinearClampCompare, float3(c.x - t, c.y - t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_Demo3DStaticShadowMap, sampler_LinearClampCompare, float3(c.x + t, c.y - t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_Demo3DStaticShadowMap, sampler_LinearClampCompare, float3(c.x - t, c.y + t, c.z))
           + SAMPLE_TEXTURE2D_SHADOW(_Demo3DStaticShadowMap, sampler_LinearClampCompare, float3(c.x + t, c.y + t, c.z));
    return lerp(1, s * 0.25, _Demo3DStaticShadowParams.z);
}

// URP's UniversalFragmentPBR without clear coat, light layers or debug views, with the main
// light's realtime shadow combined with the static shadow map. The two never double: at rung E0
// the static set is in the cascades and the map is off; at E2 the cascades hold only dynamic
// casters. min() keeps a point shaded by both from going darker than either.
half3 Demo3DLightingPBR(InputData inputData, SurfaceData surfaceData)
{
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    mainLight.shadowAttenuation = min(mainLight.shadowAttenuation, Demo3DStaticShadow(inputData.positionWS));
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
