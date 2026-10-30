// The spectrum treatment's forward pass (Spectrum.shader): its vertex and fragment programs, on
// StyleCommon.hlsl's front end, so they compile for the skinned crowd and, under SL_STATIC, for a
// structure's static mesh. Include it in the forward pass after SpectrumInput.hlsl.
SLVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    return SL_Finish(v, c, c.positionWS, c.normalWS);
}

half4 frag(SLVaryings i) : SV_Target
{
    SL_FRAGMENT_BEGIN(i, edge)
    float shatter = SL_ShatterT(SL_DEATH(i), i.bary.w);
    float3 N = normalize(i.normalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float NdotV = saturate(dot(N, V));
    float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
    float d = distance(i.positionWS.xz, _SL_HeroPos.xz);
    float hue = frac(_Hue + h01 * 0.45 + (1.0 - NdotV) * 0.5 + d * 0.035 - _SL_Beat * 0.125);
    float wave = SL_Wave(i.positionWS, 6.0);

    Light L = SL_MainLight(i.positionWS);
    float3 H = normalize(L.direction + V);
    float spec = pow(saturate(dot(N, H)), 120.0) * L.shadowAttenuation;
    float fres = pow(1.0 - NdotV, 2.0);
#if defined(SL_STATIC)
    fres *= fres * 0.35; // a wall is seen edge-on from above: the film shows at its grazing edges, the lacquer stays black
#endif
    float3 color = float3(0.008, 0.006, 0.02);
    color += SL_Hue(hue) * (0.1 + 1.5 * fres) * (0.8 + 0.6 * wave);
    color += spec * 2.5 * L.color;
    float3 addDiffuse = 0, addSpec = 0;
    SL_AdditionalLights(i.positionWS, i.positionCS, N, V, 0.0, 120.0, addDiffuse, addSpec);
    color += addSpec * 2.0;

    float wire = SL_Wire(i.bary.xyz, 1.6);
    color += SL_Hue(hue + 0.33) * wire * _WireGlow * (0.35 + 2.2 * wave);

    // Equalizer bars: bands up the body, lit up to a level set by the beat.
    float level = 0.08 + _SL_Pulse * 0.75 + _SL_Energy * 0.15;
    float bar = step(frac(h01 * 9.0), 0.3) * step(h01, level);
    color += SL_Hue(h01 * 0.6 + _Hue) * bar * 0.5;

    color += float3(1.0, 1.0, 1.0) * SL_HIT(i) + SL_Hue(hue + i.bary.w) * 2.5 * shatter;
    return half4(MixFog(SL_Grade(color), i.fogFactor), 1);
}
