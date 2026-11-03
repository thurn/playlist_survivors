// Ground telegraphs (design.md, Bosses > Every boss and The Horde > Threats; plan.md, Rendering: Telegraph): a boss's
// attacks in boss.telegraph red and the elites' moves in warning yellow, flat on the ground. Each draw is one mark
// (TelegraphRenderer.cs): a quad over its shape's bounds, the shape a signed distance in ground metres (_State.w: 0 a
// disc, 1 a ring, 2 a path, a capsule round a segment, 3 a cone, a sector). It shows
// - its outline, a thin line constant in pixels, all through the telegraph;
// - a faint wash growing from the shape's origin (the centre, the path's start, the cone's apex) to its far edge as the
//   telegraph fills (_State.x, 0 to 1), its front a brighter line that meets the outline on the landing beat;
// - at the landing, the whole shape flashing (_State.y), then fading out (_State.z).
// HDR lines bloom; outlines composite over bodies while the dim fill stays additive. No keywords. In the stills' type-ID pass, an effect's code where it draws.
Shader "Champions/Telegraph"
{
    Properties
    {
        [HDR] _Color ("Colour (linear, HDR)", Color) = (3, 0, 0, 1)
        _ChampionsId ("Type-ID pass code", Float) = 252
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" }
        Pass
        {
            Name "Telegraph"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            #include "Assets/Champions/Render/World/Shaders/World.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ChampionsId;
                float _Warning;
                float4 _A;     // xy: the centre, a path's start or a cone's apex; zw: a path's end (ground x, z)
                float4 _Size;  // x: the radius (a path's reach), y: a ring's inner radius, z: a cone's half angle, w: its facing (radians from +x)
                float4 _State; // x: fill 0-1, y: flash, z: fade 0-1, w: the shape
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 ground : TEXCOORD0;
            };

            Varyings vert(float3 positionOS : POSITION)
            {
                Varyings o;
                float3 world = TransformObjectToWorld(positionOS);
                o.positionCS = TransformWorldToHClip(world);
                o.ground = world.xz;
                return o;
            }

            // The signed distance to the shape (negative inside), and u: 0 at its origin, 1 at its far edge.
            float ShapeDistance(float2 p, out float u)
            {
                int shape = (int)round(_State.w);
                float radius = _Size.x;
                if (shape == 2)
                {
                    float2 a = _A.xy, s = _A.zw - _A.xy;
                    float t = saturate(dot(p - a, s) / max(dot(s, s), 1e-6));
                    u = t;
                    return length(p - (a + s * t)) - radius;
                }
                float2 q = p - _A.xy;
                float r = length(q);
                if (shape == 3)
                {
                    float c = cos(_Size.w), sn = sin(_Size.w);
                    float2 local = float2(q.x * c + q.y * sn, -q.x * sn + q.y * c);
                    float off = clamp(abs(atan2(local.y, local.x)) - _Size.z, -HALF_PI, HALF_PI);
                    u = r / max(radius, 1e-4);
                    return max(r - radius, r * sin(off));
                }
                if (shape == 1)
                {
                    u = (r - _Size.y) / max(radius - _Size.y, 1e-4);
                    return max(r - radius, _Size.y - r);
                }
                u = r / max(radius, 1e-4);
                return r - radius;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float u;
                float d = ShapeDistance(i.ground, u);
                float px = max(fwidth(d), 1e-4), pu = max(fwidth(u), 1e-5);
                float outline = 1 - smoothstep(0.6, 1.6, abs(d) / px);
                float inside = 1 - smoothstep(-0.5, 0.5, d / px);
                float fill = _State.x;
                float filled = inside * (1 - smoothstep(fill - pu, fill + pu, u));
                float front = inside * (1 - smoothstep(0.6, 1.6, abs(u - fill) / pu)) * step(1e-3, fill) * step(fill, 0.999);
                // Elite warnings use one stable boundary: expanding inner circles multiplied the
                // apparent hazards in a retained horde. Timing instead lifts that boundary toward impact.
                front *= 1 - _Warning;
                float local = 1 - smoothstep(5, 8, distance(i.ground, _RC_Hero.xy));
                float danger = inside * local * _Warning;
                float stripe = abs(frac((i.ground.x + i.ground.y) / .8) - .5) * .8;
                float hatch = (1 - smoothstep(.025, .025 + px, stripe)) * danger;
                float lit = (outline * lerp(1, .65 + .35 * fill, _Warning)
                    + filled * (0.01 + 0.02 * fill) * lerp(1, .2, _Warning)
                    + front + inside * _State.y * lerp(1, .04, _Warning) + hatch * .2) * _State.z;
                // Dark shoulders separate closed boundaries from amber bodies. Locally hatched
                // interiors identify the dangerous side, even when multiple warnings overlap.
                float shoulder = 1 - smoothstep(2, 4, abs(d) / px);
                float frontShoulder = inside * (1 - smoothstep(2, 4, abs(u - fill) / pu))
                    * step(1e-3, fill) * step(fill, .999) * (1 - _Warning);
                float cover = saturate(max(max(shoulder, frontShoulder), danger * .38) * _State.z);
                if (lit < 1e-3 && cover < 1e-3) discard;
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(_Color.rgb * lit, cover);
            }
            ENDHLSL
        }
    }
}
