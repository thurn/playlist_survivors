// The fire's flames treatment's forward pass (FireShell.shader): its vertex and fragment programs, on
// StyleCommon.hlsl's front end, so they compile for the skinned crowd and, under SL_STATIC, for a
// structure's static mesh. Include it in the forward pass after FireShellInput.hlsl.
SLVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    float3 p = c.positionWS, n = c.normalWS;
    float death = c.m.dissolve;
    float up = saturate(n.y * 0.6 + 0.4);
    float nz = SL_Noise(v.positionOS * 2.5 + float3(0, -_SL_Time * 2.8, 0) + c.center.w * 9.0);
    float grow = 1.0 + death * 1.5; // flares up as it burns away
    p += n * _Thickness * (0.35 + nz) * grow;
    p.y += _Lick * nz * nz * up * (1.0 + _SL_Pulse * 0.7) * grow;
    return SL_Finish(v, c, p, n);
}

half4 frag(SLVaryings i) : SV_Target
{
    CrowdLodClip(i.fx.zw, i.positionCS);
#if defined(SL_STATIC)
    SL_StaticClip(i.positionCS, i.positionWS, i.uv, true); // a burning tree's flames keep its leaves' shapes
#endif
    if (_Demo3DIdMask > 0.5) return 0; // TD15's ID render: the body alone
    float3 N = normalize(i.normalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float edge = 1.0 - abs(dot(N, V));
    float f = SL_Fbm(i.rest * 3.2 + float3(0, -_SL_Time * 3.6, 0) + SL_SEED(i) * 7.0);
    float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
    float wave = SL_Wave(i.positionWS, 6.0);
    float flame = saturate(f * 1.9 - 0.4 + (1.0 - h01) * 0.2) * (0.45 + edge * 1.1);
    flame *= (0.8 + 0.7 * wave) * (1.0 - smoothstep(0.7, 1.0, SL_DEATH(i)));
#if defined(SL_STATIC)
    flame *= smoothstep(0.3, 0.95, h01 + (f - 0.5) * 0.7); // a structure burns from its top, its faces left to the cracks
#endif
    float3 color = FireRamp(flame, _Ember.rgb, _Flame.rgb, _Core.rgb) * _Intensity;
    return half4(SL_Grade(color), 0);
}
