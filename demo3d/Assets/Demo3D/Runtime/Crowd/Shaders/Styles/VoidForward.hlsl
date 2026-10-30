// The void treatment's forward pass (Void.shader): its vertex and fragment programs, on
// StyleCommon.hlsl's front end, so they compile for the skinned crowd and, under SL_STATIC, for a
// structure's static mesh. Include it in the forward pass after VoidInput.hlsl.
SLVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    return SL_Finish(v, c, c.positionWS, c.normalWS);
}

float Stars(float2 p, float scale, float seed)
{
    float2 q = p * scale;
    float2 cell = floor(q);
    float2 f = frac(q) - 0.5;
    float h = SL_Hash(float3(cell, seed));
    float2 at = (SL_Hash3(float3(cell, seed + 1.0)).xy - 0.5) * 0.7;
    float size = 0.04 + 0.08 * SL_Hash(float3(cell, seed + 2.0));
    float twinkle = 0.6 + 0.4 * sin(_SL_Time * (2.0 + h * 5.0) + h * 40.0);
    return step(0.72, h) * smoothstep(size, 0.0, length(f - at)) * twinkle;
}

half4 frag(SLVaryings i) : SV_Target
{
    SL_FRAGMENT_BEGIN(i, edge)
    float3 N = normalize(i.normalWS);
    float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float NdotV = saturate(dot(N, V));
    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
    float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
    float2 p = suv * float2(_ScreenParams.x / _ScreenParams.y, 1.0) + nv.xy * 0.02 + SL_SEED(i) * 3.7;
    float wave = SL_Wave(i.positionWS, 6.0);

    float stars = Stars(p, 40.0, 1.0) + Stars(p, 90.0, 7.0) * 0.6 + Stars(p, 18.0, 3.0) * 1.5;
    float n = SL_Fbm(float3(p * 3.0, _SL_Time * 0.05));
    float3 nebula = lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(n * 1.6 - 0.3)) * pow(saturate(n), 2.2) * 0.9;
    float3 color = nebula + stars * (2.2 + wave * 2.5);

    float rim = pow(1.0 - NdotV, _RimPower);
    color += _RimColor.rgb * rim * (0.9 + 2.5 * wave + SL_DEATH(i) * 6.0);
    color += _RimColor.rgb * smoothstep(0.78, 0.86, 1.0 - NdotV) * 0.35;
    color += float3(1.0, 1.0, 1.0) * SL_HIT(i) * rim;
    return half4(MixFog(SL_Grade(color), i.fogFactor), 1);
}
