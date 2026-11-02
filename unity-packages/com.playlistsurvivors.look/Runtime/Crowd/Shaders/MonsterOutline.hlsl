// The monster looks' dark outline in screen space, drawn as an inverted hull, so two touching monsters
// of one type show a dark seam between them (neon-look-style.md, The Axes in 3D > Monsters): the
// solid look's (MonsterSolid.shader) and the line looks' (edge, hologram, gem), whose bright rims and
// lines alone merge a dense clump into one mass. Include after MonsterCommon.hlsl and Impulse.hlsl.
#ifndef LOOK_MONSTER_OUTLINE_INCLUDED
#define LOOK_MONSTER_OUTLINE_INCLUDED

// The outline hull's clip position: the vertex pushed out by `px` pixels (scaled from 1080 rows to
// the target's height, so a supersampled capture keeps the width), the same width at any distance,
// along its normal's projection bent halfway toward the direction from the body's `centre` (a
// low-poly body's facets split their corners' normals, and pushed apart along them alone the hull
// cracks at every crease). Drawn with front faces culled, the pushed-out back faces show as a dark
// rim round the body and over whatever lies behind it.
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

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    nointerpolation float2 lod : TEXCOORD0;
    float fogFactor : TEXCOORD1;
};

// The line looks' hull: the body skinned and, living, lifted by a passing ring's hop as their bodies
// are (ImpulseCrowd), pushed out `px` pixels round its bounding sphere's centre and `back` of the
// sphere's radius away from the camera along the view ray (which leaves it where it was on screen).
// Their light is in the rim, at the silhouette, where the hull's back faces meet the front ones at
// the same depth: unpushed, the hull covers the rim; pushed, it still covers a neighbour behind.
OutlineVaryings LineOutlineVert(MonsterAttributes v, float px, float back)
{
    MonsterVertex c = MonsterSkin(v);
    MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
    float hop, height;
    float3 light;
    ImpulseCrowd(m.position.xz, hop, light);
    float lift = (m.flags & CROWD_ALIVE) != 0 ? hop : 0;
    c.positionWS.y += lift;
    float4 sphere = CrowdSphere(m, height);
    float3 centre = sphere.xyz + float3(0, lift, 0);
    c.positionWS += normalize(c.positionWS - _WorldSpaceCameraPos) * (back * sphere.w);
    OutlineVaryings o;
    o.positionCS = MonsterOutlineCS(c.positionWS, c.normalWS, centre, px);
    o.lod = c.fx.zw;
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    return o;
}

// The hull in `colour` (void.floor), fading into the fog; nothing under the stills' ID render, so the
// mask is the bodies.
half4 LineOutlineFrag(OutlineVaryings i, float3 colour)
{
    CrowdLodClip(i.lod, i.positionCS);
    clip(0.5 - _GameIdMask);
    return half4(MixFog(colour, i.fogFactor), 1);
}

#endif
