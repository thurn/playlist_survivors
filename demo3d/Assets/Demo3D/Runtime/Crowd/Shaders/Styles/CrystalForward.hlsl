// The crystal treatment's forward pass (Crystal.shader): its vertex and fragment programs, on
// StyleCommon.hlsl's front end, so they compile for the skinned crowd and, under SL_STATIC, for a
// structure's static mesh. Include it in the forward pass after CrystalInput.hlsl.
#include "CrystalCommon.hlsl"

SLVaryings vert(SLAttributes v)
{
    SLVertex c = SL_Skin(v);
    return SL_Finish(v, c, c.positionWS, c.normalWS);
}

half4 frag(SLVaryings i) : SV_Target
{
    SL_FRAGMENT_BEGIN(i, edge)
    float shatter = SL_ShatterT(SL_DEATH(i), i.bary.w);
#if defined(SL_STATIC)
    // A breaking structure's shard (StructureShatter): its fresh inside faces, bared as the seams open,
    // glow with the gem's light while it breaks (_SL_PieceFx.w).
    shatter = max(shatter, SL_INSIDE(i) * _SL_PieceFx.w);
#endif
    // A broken structure's shard shows its fresh inside faces; the crowd has none.
    return CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, SL_HIT(i), shatter, SL_INSIDE(i), i.fogFactor);
}
