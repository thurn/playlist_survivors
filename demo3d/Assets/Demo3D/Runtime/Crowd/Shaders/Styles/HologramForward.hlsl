// The hologram treatment's forward pass (Hologram.shader): its vertex and fragment programs, on
// StyleCommon.hlsl's front end, so they compile for the skinned crowd and, under SL_STATIC, for a
// structure's static mesh. Include it in the forward pass after HologramInput.hlsl.
SLVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    return SL_Finish(v, c, c.positionWS, c.normalWS);
}

half4 frag(SLVaryings i) : SV_Target
{
    SL_FRAGMENT_BEGIN(i, scanEdge)
    float h = i.rest.y;
    float3 N = normalize(i.normalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float fres = pow(1.0 - saturate(dot(N, V)), 1.6);
    float scan = 0.55 + 0.45 * sin(i.positionWS.y * 110.0 - _SL_Time * 8.0);
    float roll = exp(-abs(frac(h * 0.45 - _SL_Time * 0.55) - 0.5) * 18.0);
    float flicker = 0.82 + 0.18 * SL_Hash1(floor(_SL_Time * 24.0) + SL_SEED(i) * 7.0);
    float wave = SL_Wave(i.positionWS, 6.0);
    float wire = SL_Wire(i.bary.xyz, 1.2);

    float3 color = _Color.rgb * ((0.05 + fres * 1.1) * scan + roll * 0.5 + wire * 0.3);
    float3 vn = mul((float3x3)UNITY_MATRIX_V, N);
    color *= float3(1.0 + vn.x * 0.9, 1.0, 1.0 - vn.x * 0.9);
    color *= flicker * (0.75 + 1.0 * wave);
    color += float3(1.5, 1.5, 1.5) * SL_HIT(i) * fres + _Color.rgb * 4.0 * scanEdge;
    // Added light fades into the fog rather than glowing through it.
#if defined(SL_STATIC)
    // A projected structure is dim glass under its light (premultiplied: the structure shader blends One
    // OneMinusSrcAlpha), so a see-through wall or canopy doesn't uncover the lighter ground behind the
    // horde it stands beside.
    return half4(MixFogColor(SL_Grade(color), half3(0, 0, 0), i.fogFactor), SL_STATIC_HOLOGRAM_DIM);
#else
    return half4(MixFogColor(SL_Grade(color), half3(0, 0, 0), i.fogFactor), 0);
#endif
}
