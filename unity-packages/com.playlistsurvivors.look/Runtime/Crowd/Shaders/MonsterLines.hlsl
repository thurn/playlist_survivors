// Pixel-width lines on a crowd mesh's triangle edges, from its barycentrics (TEXCOORD3) and the
// edges each look draws (the feature edges in TEXCOORD5, or all of them): the edge look's lines
// (MonsterEdge.hlsl) and the hologram's (MonsterHologram.hlsl).
#ifndef LOOK_MONSTER_LINES_INCLUDED
#define LOOK_MONSTER_LINES_INCLUDED

// How lit a feature edge is at this pixel: 1 on a flagged edge, falling off over a pixel at
// _EdgePx from it. A barycentric's screen derivative makes the width the same in pixels at any
// distance, as a neon tube's would be. Widths are pixels at 1080 rows of the frame drawn, so a
// supersampled capture (drawn at 2160 rows and downsampled) draws the lines a live run and the game
// do rather than half as wide. A triangle only a few pixels across (a far or finely cut part) fades
// its lines, so small detail doesn't turn the body into a tangle of wire.
float EdgeLine(float3 bary, float3 edges, float px)
{
    float rows = max(1.0, _ScaledScreenParams.y / 1080.0); // render rows: a supersampled frame's, not its output's
    float3 fw = max(fwidth(bary), 1e-5);
    float3 w = bary / fw;
    px *= rows;
    float3 on = (1 - smoothstep(px - 0.75 * rows, px + 0.75 * rows, w)) * edges;
    float size = 1 / (max(fw.x, max(fw.y, fw.z)) * rows); // the triangle's height in pixels at 1080 rows, roughly
    return max(on.x, max(on.y, on.z)) * lerp(0.35, 1, saturate((size - 1.5) / 4));
}

#endif
