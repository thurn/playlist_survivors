// The monsters' dark outline in screen space, drawn as an inverted hull, so two touching monsters of one type show a
// dark seam between them where their bright rims and lines alone would merge a clump into one mass; and a body's
// radius on screen, which the rim scales by. Ported by review from the frozen look package's MonsterOutline.hlsl.
// Include after MonsterCommon.hlsl.
#ifndef CHAMPIONS_MONSTER_OUTLINE_INCLUDED
#define CHAMPIONS_MONSTER_OUTLINE_INCLUDED

// The outline hull's clip position: the vertex pushed out by `px` pixels (scaled from 1080 rows to the target's
// height, so a supersampled capture keeps the width), the same width at any distance, along its normal's projection
// bent halfway toward the direction from the body's `centre` (a low-poly body's facets split their corners' normals,
// and pushed apart along them alone the hull cracks at every crease). Drawn with front faces culled, the pushed-out
// back faces show as a dark rim round the body and over whatever lies behind it.
float4 MonsterOutlineCS(float3 positionWS, float3 normalWS, float3 centre, float px)
{
    float4 cs = TransformWorldToHClip(positionWS);
    float3 away = positionWS - centre;
    float3 dir = normalWS + (dot(away, away) > 1e-8 ? normalize(away) : 0);
    float2 n = mul((float3x3)UNITY_MATRIX_VP, dir).xy;
    float len = length(n);
    if (len > 1e-5)
    {
        px *= max(1.0, _ScreenParams.y / 1080.0);
        cs.xy += n / len * (2.0 * px / _ScreenParams.xy) * cs.w;
    }
    return cs;
}

// A body's radius on screen in pixels at 1080 rows, from its bounding sphere (CrowdSphere): its centre and the point
// a radius above it along the camera's up axis, projected.
float MonsterRadiusPx(float4 sphere)
{
    float4 c = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
    float4 t = mul(UNITY_MATRIX_VP, float4(sphere.xyz + sphere.w * UNITY_MATRIX_I_V._m01_m11_m21, 1));
    return abs(t.y / max(t.w, 1e-3) - c.y / max(c.w, 1e-3)) * 0.5 * 1080.0;
}

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    nointerpolation float2 lod : TEXCOORD0;
};

// The hull: the body skinned and, living, lifted by the world's hop as its body is (MonsterWorld), pushed out `px`
// pixels round its bounding sphere's centre and `back` of the sphere's radius away from the camera along the view ray
// (which leaves it where it was on screen). At `back` 0 the hull takes the body's outer rim, a dark seam inside each
// silhouette as well as over the neighbour behind.
OutlineVaryings LineOutlineVert(MonsterAttributes v, float px, float back)
{
    MonsterVertex c = MonsterSkin(v);
    MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
    float hop, pulse, height;
    float3 light;
    MonsterWorld(m.position.xz, hop, light, pulse);
    float lift = (m.flags & CROWD_ALIVE) != 0 ? hop : 0;
    c.positionWS.y += lift;
    float4 sphere = CrowdSphere(m, height);
    float3 centre = sphere.xyz + float3(0, lift, 0);
    c.positionWS += normalize(c.positionWS - _WorldSpaceCameraPos) * (back * sphere.w);
    OutlineVaryings o;
    o.positionCS = MonsterOutlineCS(c.positionWS, c.normalWS, centre, px);
    o.lod = c.fx.zw;
    return o;
}

// The hull in `colour` (the glass's); nothing in the stills' type-ID pass, so its codes are the bodies'.
half4 LineOutlineFrag(OutlineVaryings i, float3 colour)
{
    CrowdLodClip(i.lod, i.positionCS);
    clip(0.5 - _ChampionsIdPass);
    return half4(colour, 1);
}

#endif
