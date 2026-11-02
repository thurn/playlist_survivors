// The hero's slashes, waves, smears and spectral swords (design.md, The Look > Weapon effects; Effects.cs): Hovl's slash
// shapes drawn through our own shader, never Hovl's. Each instance is one Shape record (Effects.hlsl), drawn at the
// frame's song time:
// - an arc (SHAPE_ARC): one of Hovl's ring meshes, normalised to a unit ring by its frame (_RingFrame, the baker's), shown
//   only within the shape's arc round its facing. Its head sweeps the arc fast-in, slow-out (an ease-out over headIn of
//   its life); the lit band, a thin bright core at the outer edge over a faint body, comes to a point at the head and
//   narrows to nothing behind it (Hovl's crescent taper; an even band for a ring); Hovl's noise texture streaks it along
//   the arc and dissolves it as it fades;
// - a ribbon (SHAPE_RIBBON, SHAPE_UPRIGHT): the same sweep, taper and streaks along a flat strip, its core down the middle;
// - a solid (SHAPE_SOLID): a Hovl mesh (the magic sword) glowing at its rim, a spectral sword.
// White core, faint white-blue edge (the palette's hero.blade and hero.edge), additive in HDR so bright cores bloom. In the
// stills' type-ID pass, an effect's code where it is lit.
Shader "Champions/Slash"
{
    Properties
    {
        _Noise ("Noise (Hovl's)", 2D) = "gray" {}
        _CoreColor ("Core (linear)", Color) = (1, 1, 1, 1)
        _EdgeColor ("Edge (linear)", Color) = (0.77, 0.85, 1, 1)
        _ChampionsId ("Type-ID pass code", Float) = 252
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Slash"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            #include "Effects.hlsl"

            StructuredBuffer<Shape> _Shapes;
            float4x4 _RingFrame;  // the mesh into its unit frame: a ring round +y in the xz plane, outer radius 1; a solid's length along +x
            float _RingInner;     // the ring's inner radius in that frame
            TEXTURE2D(_Noise);
            SAMPLER(sampler_Noise);
            float4 _Noise_ST;
            half4 _CoreColor, _EdgeColor;
            float _ChampionsId;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 local : TEXCOORD0;      // the unit frame's point (an arc's), or (along, across, 0) on a ribbon
                float2 uv : TEXCOORD1;
                nointerpolation uint instance : TEXCOORD2;
                float rim : TEXCOORD3;          // a solid's rim light
            };

            // The ground basis of a shape facing f: forward, its left on the ground, up.
            void Basis(float2 f, out float3 forward, out float3 left)
            {
                forward = float3(f.x, 0, f.y);
                left = float3(-f.y, 0, f.x);
            }

            // How far it has travelled and grown: linear over its travel (a solid falls, easing in).
            float Travel(Shape s, float age)
            {
                float t = saturate(age / max(s.life * s.travelIn, 1e-4));
                return (int)s.mode == SHAPE_SOLID ? t * t : t;
            }

            Varyings vert(Attributes v)
            {
                Shape s = _Shapes[v.instanceID];
                int mode = (int)s.mode;
                float age = _EffectNow - s.born;
                float t = Travel(s, age);
                float scale = s.radius * lerp(s.grow, 1, 1 - (1 - t) * (1 - t));
                float3 forward, left;
                Basis(s.facing, forward, left);
                float3 up = float3(0, 1, 0);
                float3 world;
                float3 local;
                float rim = 0;
                if (mode == SHAPE_ARC || mode == SHAPE_SOLID)
                {
                    local = mul(_RingFrame, float4(v.positionOS, 1)).xyz;
                    float3 n = normalize(mul((float3x3)_RingFrame, v.normalOS));
                    float c = cos(s.tilt), sn = sin(s.tilt);
                    float3 axisX, axisY, axisZ;
                    if (mode == SHAPE_ARC)
                    {
                        // Tilted about its facing: one side of the ring rises.
                        axisX = forward;
                        axisY = up * c + left * sn;
                        axisZ = left * c - up * sn;
                    }
                    else
                    {
                        // A solid's +x points along its facing, pitched up by its tilt (down when negative).
                        axisX = forward * c + up * sn;
                        axisY = up * c - forward * sn;
                        axisZ = left;
                    }
                    float3 p = local * scale;
                    world = s.position + axisX * p.x + axisY * p.y + axisZ * p.z;
                    float3 nw = normalize(axisX * n.x + axisY * n.y + axisZ * n.z);
                    float3 view = normalize(_WorldSpaceCameraPos - world);
                    rim = 1 - abs(dot(nw, view));
                }
                else
                {
                    // The strip's mesh: x along it (0 to 1), y across (-1 to 1).
                    local = float3(v.positionOS.x, v.positionOS.y, 0);
                    float3 across = mode == SHAPE_UPRIGHT ? normalize(UNITY_MATRIX_V[0].xyz) : left;
                    float3 along = mode == SHAPE_UPRIGHT ? up : forward;
                    world = s.position + along * (v.positionOS.x * s.radius) + across * (v.positionOS.y * s.arc);
                }
                world += up * s.height + s.travel * t;
                Varyings o;
                o.positionCS = TransformWorldToHClip(world);
                o.local = local;
                o.uv = v.uv;
                o.instance = v.instanceID;
                o.rim = rim;
                return o;
            }

            float Noise(float2 uv) { return SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).r; }

            half4 frag(Varyings i) : SV_Target
            {
                Shape s = _Shapes[i.instance];
                int mode = (int)s.mode;
                float age = _EffectNow - s.born;
                float p = age / s.life;
                clip(p);
                clip(1 - p);
                // Fast in, slow out: a sharp attack, the head's ease-out, then the fade's last share of its life.
                float envelope = saturate(age / 0.012) * pow(saturate((1 - p) / max(s.fade, 1e-3)), 1.5);
                float intensity;
                float dissolveAt = saturate((p - (1 - s.fade)) / max(s.fade, 1e-3));
                if (mode == SHAPE_SOLID)
                {
                    intensity = (0.2 + 0.8 * pow(saturate(i.rim), 2.0)) * envelope;
                }
                else
                {
                    float along, edge, repeats;
                    if (mode == SHAPE_ARC)
                    {
                        float a = atan2(i.local.z, i.local.x);
                        float span = min(s.arc, 2 * PI);
                        along = a / span + 0.5;
                        clip(along);
                        clip(1 - along);
                        float r = length(i.local.xz);
                        edge = saturate((r - _RingInner) / max(1 - _RingInner, 1e-3)); // 0 at the inner edge, 1 at the outer
                        repeats = max(1, round(span * 0.5)); // whole repeats, so a full ring's noise has no seam
                    }
                    else
                    {
                        along = i.local.x;
                        edge = 1 - abs(i.local.y); // the core down the middle
                        repeats = 2;
                    }
                    if (((uint)s.flags & SHAPE_MIRRORED) != 0) along = 1 - along;
                    float head = 1 - pow(1 - saturate(p / max(s.headIn, 1e-3)), 3);
                    float behind = head - along;
                    clip(behind + 0.01);
                    // Hovl's crescent taper: a point at the head, widest just behind it, narrowing to nothing at the tail;
                    // an even band without it (a shockwave's ring, a figure).
                    float u = saturate(behind / 0.9);
                    float crescent = smoothstep(0, 0.12, u) * pow(1 - u, 0.8) / 0.9;
                    float profile = lerp(1, crescent, s.taper);
                    float width = s.width * profile;
                    float band = (edge - (1 - width)) / max(width, 1e-3);
                    clip(band);
                    // Lit from the outer edge in: a thin bright core at the edge, the rest of the band faint.
                    float across = smoothstep(0, 0.25, band) * (0.12 + 0.88 * pow(band, 4));
                    float ends = mode == SHAPE_UPRIGHT ? pow(sin(PI * saturate(along)), 0.6) : 1;
                    // Hovl's noise as streaks along the sweep, drifting back along it, and eating it as it fades.
                    float2 nuv = TRANSFORM_TEX(float2(along * repeats - age * 0.8, edge * 2.5 + s.born * 0.37), _Noise);
                    float n = Noise(nuv);
                    float streaks = lerp(1, 0.45 + 1.1 * n, s.streak);
                    float dissolve = smoothstep(dissolveAt - 0.12, dissolveAt + 0.04, Noise(nuv * 0.5 + 0.31));
                    intensity = across * ends * streaks * dissolve * envelope * saturate((behind + 0.01) / 0.02);
                }
                float lit = intensity * s.brightness;
                if (_ChampionsIdPass > 0.5) clip(lit - 0.02); // the pass marks the shape down to its faint body, not the empty mesh
                CHAMPIONS_ID_PASS(_ChampionsId)
                half3 colour = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(intensity * 1.4 - 0.45));
                return half4(colour * lit, 1);
            }
            ENDHLSL
        }
    }
}
