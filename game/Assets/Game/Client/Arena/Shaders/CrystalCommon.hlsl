// What the crystal shaders share (unity-game.md, "The Look"): the crystal structures
// (StructureCrystal) and the crystal monsters (MonsterCrystal). demo3d's StyleCommon.hlsl's beat
// globals, noise, beat wave, lighting helpers, facet edges and luminance grade, copied and owned.
// Include it after URP's Core.hlsl and Lighting.hlsl and StaticShadow.hlsl, in the SubShader's
// HLSLINCLUDE.
#ifndef GAME_CRYSTAL_COMMON_INCLUDED
#define GAME_CRYSTAL_COMMON_INCLUDED

// The beat globals, shared by the structures and the crowd (demo3d's StyleBeat publishes them from
// the music each frame; ArenaRuntime sets them at rest until the look's beat feel drives them).
float _SL_Time;      // the song's time, seconds
float _SL_Beat;      // beats since the song's first beat, fractional
float _SL_Pulse;     // 1 on each beat, decaying
float _SL_BarPulse;  // 1 on each downbeat, decaying
float _SL_Energy;    // the music's energy at this beat, 0..1
float4 _SL_HeroPos;  // the hero's feet, world space
float _SL_WaveSpeed; // how far the beat wave travels in one beat, metres

// ---------------------------------------------------------------------------------------------
// Noise.

float SL_Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float SL_Hash1(float n) { return frac(sin(n) * 43758.5453); }

float3 SL_Hash3(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453);
}

float SL_Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// ---------------------------------------------------------------------------------------------
// The beat wave: a ring that leaves the hero on every beat and sweeps out across the arena and the horde.
// 1 where the ring is passing now, decaying behind it (sharpness: how fast).

float SL_Wave(float3 positionWS, float sharpness)
{
    float d = distance(positionWS.xz, _SL_HeroPos.xz);
    float phase = frac(_SL_Beat - d / max(_SL_WaveSpeed, 0.01));
    return exp(-phase * sharpness);
}

// The wave's distance-delayed beat count, for things that change once per beat as it passes.
float SL_WaveBeat(float3 positionWS)
{
    float d = distance(positionWS.xz, _SL_HeroPos.xz);
    return floor(_SL_Beat - d / max(_SL_WaveSpeed, 0.01));
}

// ---------------------------------------------------------------------------------------------
// Lighting.

// The moon, its realtime shadow combined with the arena's static shadow map.
Light SL_MainLight(float3 positionWS)
{
    Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
    light.shadowAttenuation = min(light.shadowAttenuation, GameStaticShadow(positionWS));
    return light;
}

void SL_AddLight(Light light, float3 N, float3 V, float wrap, float specPower, inout float3 diffuse, inout float3 specular)
{
    float3 c = light.color * light.distanceAttenuation * light.shadowAttenuation;
    float nl = dot(N, light.direction);
    diffuse += c * saturate((nl + wrap) / (1.0 + wrap));
    float3 H = normalize(light.direction + V);
    specular += c * pow(saturate(dot(N, H)), specPower) * saturate(nl * 4.0);
}

// Every additional light (the hero's light, the flames, the effects' lights) through Forward+'s
// cluster loop: wrapped diffuse and a Blinn-Phong highlight of the given power, summed.
void SL_AdditionalLights(float3 positionWS, float4 positionCS, float3 N, float3 V, float wrap,
                         float specPower, inout float3 diffuse, inout float3 specular)
{
#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = N;
    inputData.viewDirectionWS = V;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        SL_AddLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), N, V, wrap, specPower, diffuse, specular);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        SL_AddLight(GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1)), N, V, wrap, specPower, diffuse, specular);
    LIGHT_LOOP_END
#endif
}

// A thin line along every facet edge, about `pixels` wide.
float SL_Wire(float3 bary, float pixels)
{
    float e = min(bary.x, min(bary.y, bary.z));
    float w = fwidth(e) * pixels;
    return 1.0 - smoothstep(w * 0.5, w, e);
}

// A soft limit on luminance after a gain, keeping the hue: the set's and the horde's grades build on it.
float3 SL_GradeOf(float3 color, float peak) { return color / (1.0 + Luminance(color) / peak); }

#endif
