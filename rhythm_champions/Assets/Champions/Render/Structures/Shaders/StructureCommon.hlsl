// The structures' draw-side code (design.md, The World > Structures, The Look > Edge cuts; plan.md, Rendering), shared
// by StructureFill and StructureLines: an instance of a type, its pieces' transforms and the look's inputs.
// - A standing structure is an instance at its middle on the ground, turned by the sim's angle, its pieces at rest
//   (pieceBase 0, the identity block); a broken one's shards are an instance at the origin whose pieces carry their
//   whole flight (StructureShatter writes them from its PhysX bodies each frame).
// - The fill takes the zone's fill colour and the lines its edge colour where each vertex stands (World.hlsl:
//   crossfading behind the zone sweep), the lines flare as the beat wave passes, and a hit or a break flashes them
//   through LookFlash (calm outside special moments).
// - Below the ground nothing draws: Synty's foundations and the sinking shards.
#ifndef CHAMPIONS_STRUCTURE_COMMON_INCLUDED
#define CHAMPIONS_STRUCTURE_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Assets/Champions/Render/Shaders/IdPass.hlsl"
#define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
#include "UnityIndirect.cginc"
#include "Assets/Champions/Render/World/Shaders/World.hlsl"

// 32 bytes, matching StructureInstance.cs.
struct StructureInstance
{
    float3 position;  // its middle on the ground (world); the origin for shards
    float angle;      // the sim's angle: radians from +x toward +z, seen from above
    float flashAt;    // the song time of its latest hit, or its break for shards
    float flash;      // that flash's strength (0 none)
    uint pieceBase;   // its first piece in _StructurePieces (0: the identity block)
    float fade;       // 1 standing; shards fade as they sink
};

StructuredBuffer<StructureInstance> _StructureInstances;
StructuredBuffer<float4> _StructurePieces;  // three rows a piece: from the structure's own space to its instance's
float4 _StructureLook;   // x the fill's gain, y the edges' gain, z the beat wave's flare on the edges, w the flash's lift
float4 _StructureLines;  // x the lines' width in pixels at 1080 rows, y their lift toward the camera (m)
float _ChampionsId;      // the draw's code in the stills' type-ID pass: a structure's, or the shards' effect code

#define STRUCTURE_FLASH_SECONDS 0.2

StructureInstance InstanceOf(uint instanceID)
{
    InitIndirectDrawArgs(0);
    return _StructureInstances[GetIndirectInstanceID_Base(instanceID)];
}

// A point of the structure's own space, moved with its piece and placed in the world.
float3 PlaceStructure(StructureInstance s, float3 p, float piece)
{
    uint at = (s.pieceBase + (uint)piece) * 3;
    float3 q = mul(float3x4(_StructurePieces[at], _StructurePieces[at + 1], _StructurePieces[at + 2]), float4(p, 1));
    float sa, ca;
    sincos(s.angle, sa, ca);
    return s.position + float3(q.x * ca - q.z * sa, q.y, q.x * sa + q.z * ca);
}

// The instance's flash now (0 to 1): its latest hit's, fading over STRUCTURE_FLASH_SECONDS, full in a special moment.
float FlashOf(StructureInstance s)
{
    float since = _RC_Clock.x - s.flashAt;
    return since >= 0 ? s.flash * saturate(1 - since / STRUCTURE_FLASH_SECONDS) * LookFlash(0.5) : 0;
}

#endif
