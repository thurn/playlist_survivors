// The structures' edge look (design.md, The World > Structures; plan.md, Rendering > The edge look), shared by the
// standing bodies (StructureEdge.shader) and their shards (StructureEdgeShard.shader): an instance of a type, the
// shards' pieces, and the surface.
// - A standing structure is an instance at its middle on the ground, turned by the sim's angle; a break's shards are
//   an instance at the origin whose pieces carry their whole flight (StructureShatter writes them from its PhysX bodies
//   each frame, three rows a piece).
// - The surface: the zone's dim fill where it stands (World.hlsl: crossfading behind the zone sweep), flat and calibrated
//   as drawn through the grade as the floor is (Ungraded), dark enough that the lines carry the shape; over it the monsters' feature
//   lines (MonsterLines.hlsl, pixel-wide on the flagged edges) in the zone's edge colour, flaring as the beat wave
//   passes; a hit or a break lifts both toward the edge colour through LookFlash (calm outside special moments).
// - Below the ground nothing draws: Synty's foundations and the sinking shards.
// The shader declares its UnityPerMaterial CBUFFER (_Fill, _Edge, _EdgePx, _Beat, _Flash: StructureGains)
// before including this.
#ifndef CHAMPIONS_STRUCTURE_COMMON_INCLUDED
#define CHAMPIONS_STRUCTURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Champions/Render/Shaders/IdPass.hlsl"
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "Assets/Champions/Render/World/Shaders/World.hlsl"
#include "Assets/Champions/Render/Crowd/Shaders/MonsterLines.hlsl"

// 32 bytes, matching StructureInstance.cs.
struct StructureInstance
{
    float3 position;  // its middle on the ground (world); the origin for shards
    float angle;      // the sim's angle: radians from +x toward +z, seen from above
    float flashAt;    // the song time of its latest hit, or its break for shards
    float flash;      // that flash's strength (0 none)
    uint pieceBase;   // a break's first piece in _StructurePieces
    float fade;       // 1 standing; shards fade as they sink
};

StructuredBuffer<StructureInstance> _StructureInstances;
StructuredBuffer<float4> _StructurePieces;  // three rows a piece: from the structure's own space to the world
float _ChampionsId;      // the draw's code in the stills' type-ID pass: a structure's, or the shards' effect code

#define STRUCTURE_FLASH_SECONDS 0.2

StructureInstance InstanceOf(uint instanceID)
{
    InitIndirectDrawArgs(0);
    return _StructureInstances[GetIndirectInstanceID_Base(instanceID)];
}

// A point of the structure's own space placed in the world, turned by its angle.
float3 PlaceStructure(StructureInstance s, float3 p)
{
    float sa, ca;
    sincos(s.angle, sa, ca);
    return s.position + float3(p.x * ca - p.z * sa, p.y, p.x * sa + p.z * ca);
}

// A point of the structure's own space moved with its piece (a break's shards).
float3 PlacePiece(StructureInstance s, float3 p, float piece)
{
    uint at = (s.pieceBase + (uint)piece) * 3;
    return mul(float3x4(_StructurePieces[at], _StructurePieces[at + 1], _StructurePieces[at + 2]), float4(p, 1));
}

// The instance's flash now (0 to 1): its latest hit's, fading over STRUCTURE_FLASH_SECONDS, full in a special moment.
float FlashOf(StructureInstance s)
{
    float since = _RC_Clock.x - s.flashAt;
    return since >= 0 ? s.flash * saturate(1 - since / STRUCTURE_FLASH_SECONDS) * LookFlash(0.5) : 0;
}

// How lit the line where a standing structure meets the ground is at this pixel, as wide as the edges (EdgeLine's
// measure): its foundation goes under the ground, so without it a wall or a boulder's outline stays open at its base.
float GroundLine(float y)
{
    float rows = _ScaledScreenParams.y / 2160.0;
    float px = _EdgePx * rows;
    float w = y / max(fwidth(y), 1e-5);
    return 1 - smoothstep(px - 0.75 * rows, px + 0.75 * rows, w);
}

// The surface at a pixel: the fill and the lines (`lift` raises the lines: the beat wave's flare, a fresh crack's heat;
// `ground` 1 draws the line along the ground, for a standing body).
float3 StructureSurface(float3 positionWS, float3 bary, float3 edges, float lift, float flash, float ground)
{
    float3 fill = Ungraded(ZoneColour(positionWS.xz, ZONE_FILL)) * _Fill;
    float3 edge = ZoneColour(positionWS.xz, ZONE_EDGE);
    float lit = max(EdgeLine(bary, edges, _EdgePx), ground * GroundLine(positionWS.y));
    // A flash lights the lines, as a monster's hit does; the fill lifts a little, so the body stays a dim solid.
    float3 color = lerp(fill, edge, saturate(flash * _Flash * 0.2));
    return color + edge * (_Edge * lit * (1 + lift) * (1 + flash * _Flash));
}

#endif
