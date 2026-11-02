#ifndef CHAMPIONS_ID_PASS_INCLUDED
#define CHAMPIONS_ID_PASS_INCLUDED

// The stills' type-ID pass (Champions.Render.IdPass): with the global _ChampionsIdPass at 1, a fragment writes its
// draw's code (_ChampionsId, a per-material or per-draw float) out of 255 in red instead of its colour. A global float,
// not a keyword, so it costs no shader variants.
float _ChampionsIdPass;

#define CHAMPIONS_ID_PASS(id) if (_ChampionsIdPass > 0.5) return half4((id) / 255.0, 0, 0, 1);

#endif
