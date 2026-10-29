// The effect runtime's draw (effects.md, The runtime): procedural quads, beams and meshes, one
// indirect draw per bucket, expanded from the particle pool in the vertex shader. Every blend
// mode the converted materials use is drawn with premultiplied blending (One,
// OneMinusSrcAlpha): additive materials write alpha 0, alpha-blended ones premultiply. The
// shading is written from scratch from the materials' observed behaviour: texture times vertex
// colour times an HDR tint, optional flipbook, a scrolling noise mask and soft particles
// against the scene depth.
Shader "Demo3D/EffectParticles"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "EffectCommon.hlsl"

        StructuredBuffer<Particle> _Particles;
        StructuredBuffer<Instance> _Instances;
        StructuredBuffer<uint> _DrawList;
        StructuredBuffer<uint2> _SortList;
        StructuredBuffer<float4> _MeshVerts;
        TEXTURE2D_ARRAY(_Textures);
        SAMPLER(sampler_Textures);
        uint _Bucket, _BucketCap, _UseSort, _MeshFirst, _SortSize;
        float _Now;
        float4x4 _OverdrawVP;
        float4 _EffCamPos, _EffCamRight, _EffCamUp, _EffCamFwd; // set by EffectSystem each frame; _EffCamFwd.w is tan(vfov / 2)

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float4 color : COLOR;
            float4 uv : TEXCOORD0;      // main uv, noise uv
            nointerpolation uint4 info : TEXCOORD1; // emitter, slice, noise slice, flags
            float4 extra : TEXCOORD2;   // eye depth, softness, premultiplied, custom.x
        };

        float3 ParticleWorld(Particle p, uint e, uint flags, out Frame f)
        {
            Instance inst = _Instances[asuint(p.rotInst.w)];
            f = EmitterFrame(inst, e, _Now);
            return (flags & F_LOCAL) ? FramePoint(f, p.posAge.xyz) : p.posAge.xyz;
        }

        float3 WorldVelocity(Particle p, uint flags, Frame f)
        {
            return (flags & F_LOCAL) ? FrameVector(f, p.vEff.xyz) : p.vEff.xyz;
        }

        float2 Rot2(float2 v, float a)
        {
            float s, c;
            sincos(a, s, c);
            return float2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        Varyings Expand(uint vid, uint iid, bool overdraw)
        {
            Varyings o = (Varyings)0;
            uint at = _Bucket * _BucketCap + iid;
            // Only the first _SortSize alpha billboards were sorted this frame; the rest draw unsorted.
            uint slot = _UseSort && iid < _SortSize ? _SortList[iid].y : _DrawList[at];
            if (slot == 0xFFFFFFFFu) { o.positionCS = float4(0, 0, -1, 1); return o; }
            Particle p = _Particles[slot];
            uint e = asuint(p.misc.x);
            uint flags = EFlags(e);
            float t = saturate(p.posAge.w / p.velLife.w);
            float rnd = p.misc.z;
            Frame f;
            float3 center = ParticleWorld(p, e, flags, f);
            float4 r0 = EM(e, S_RENDER0), r1 = EM(e, S_RENDER1), tex = EM(e, S_TEX);
            uint mode = (uint)r0.x, align = (uint)r0.y;

            float3 size = p.sizeSeed.xyz;
            if (flags & F_SIZELIFE)
            {
                float4 s = Lut2(e, R_SIZE, R_SIZE2, (flags & F_SIZE2) != 0, t, rnd);
                size *= (flags & F_SIZEAXES) ? s.xyz : s.xxx;
            }
            float3 vel = WorldVelocity(p, flags, f);
            if (flags & F_SIZESPEED)
            {
                float4 m = EM(e, S_MOD);
                size *= LutRow(ERow(e) + R_MISC2, saturate((length(vel) - m.z) / max(m.w - m.z, 1e-4))).x;
            }
            float4 col = p.color;
            if (flags & F_COLORLIFE) col *= Lut2(e, R_COLOR, R_COLOR2, (flags & F_COLOR2) != 0, t, rnd);

            float3 camRight = _EffCamRight.xyz, camUp = _EffCamUp.xyz;
            float3 camPos = _EffCamPos.xyz;
            float3 world;
            float2 uv;
            if (mode == RM_MESH)
            {
                uint v = _MeshFirst + vid;
                float4 a = _MeshVerts[v * 3], b = _MeshVerts[v * 3 + 1], c = _MeshVerts[v * 3 + 2];
                float3 rot = (flags & (F_ROT3D | F_ROTAXES)) ? p.rotInst.xyz : float3(0, 0, p.rotInst.z);
                float3 local = RotateEuler(rot, a.xyz * size);
                if (align == 2) local = RotateY(f.yaw, RotateQ(f.childRot, local));
                world = center + local;
                uv = float2(a.w, b.w);
                col *= c;
            }
            else
            {
                const float2 corners[6] = { float2(-0.5, -0.5), float2(0.5, -0.5), float2(0.5, 0.5), float2(-0.5, -0.5), float2(0.5, 0.5), float2(-0.5, 0.5) };
                float2 q = corners[vid % 6];
                uv = q + 0.5;
                if (mode == RM_BEAM)
                {
                    float3 axis = normalize(FrameVector(f, float3(0, 0, 1)) + 1e-6);
                    float len = r0.z * f.scale;
                    float3 side = normalize(cross(axis, camPos - center) + 1e-6);
                    world = center + axis * (q.y + 0.5) * len + side * q.x * size.x;
                    uv = float2(r0.w > 0 ? (q.y + 0.5) * len * r0.w : q.y + 0.5, q.x + 0.5);
                }
                else if (mode == RM_STRETCH)
                {
                    float spd = length(vel);
                    float3 dir = spd > 1e-5 ? vel / spd : camUp;
                    float len = size.x * r0.z + spd * r0.w;
                    float3 side = normalize(cross(dir, camPos - center) + 1e-6);
                    world = center + dir * (align == 3 ? q.x - 0.5 : q.x) * len + side * q.y * size.x;
                }
                else
                {
                    float2 sq = Rot2(q * size.xy, -p.rotInst.z);
                    float3 ax, ay;
                    if (mode == RM_HORIZONTAL) { ax = float3(1, 0, 0); ay = float3(0, 0, 1); }
                    else if (mode == RM_VERTICAL) { ay = float3(0, 1, 0); ax = normalize(cross(ay, camPos - center) + 1e-6); }
                    else if (align == 2)
                    {
                        float3 rot = (flags & (F_ROT3D | F_ROTAXES)) ? float3(p.rotInst.xy, 0) : 0;
                        ax = RotateY(f.yaw, RotateQ(f.childRot, RotateEuler(rot, float3(1, 0, 0))));
                        ay = RotateY(f.yaw, RotateQ(f.childRot, RotateEuler(rot, float3(0, 1, 0))));
                    }
                    else { ax = camRight; ay = camUp; }
                    // Minimum and maximum particle size, as fractions of the view's height.
                    float dist = max(0.01, dot(center - camPos, _EffCamFwd.xyz));
                    float viewH = 2 * dist * _EffCamFwd.w;
                    float s = max(size.x, size.y);
                    float clampK = s > 0 ? clamp(s, r1.x * viewH, max(r1.y, 1e-4) * viewH) / s : 1;
                    world = center + (ax * sq.x + ay * sq.y) * clampK;
                }
            }

            // Flipbook (whole sheet).
            if (flags & F_SHEET)
            {
                float tiles = tex.y * tex.z;
                float frame = floor(Lut(e, R_MISC, t).w * tiles * tex.w + p.misc.y);
                frame = fmod(frame, tiles);
                float fx = fmod(frame, tex.y), fy = tex.z - 1 - floor(frame / tex.y);
                uv = (uv + float2(fx, fy)) / tex.yz;
            }
            float4 m1 = EM(e, S_MAT1), m2 = EM(e, S_MAT2), m0 = EM(e, S_MAT0);
            float age = p.posAge.w;
            float2 noiseUv = uv + m2.xy * age;
            uv += m2.zw * age;
            float custom = 0;
            if (flags & F_CUSTOM) custom = Lut(e, R_CUSTOM, t).x;

            o.positionCS = overdraw ? mul(_OverdrawVP, float4(world, 1)) : TransformWorldToHClip(world);
            o.color = float4(col.rgb * m0.rgb, col.a * m0.a);
            o.uv = float4(uv, noiseUv);
            o.info = uint4(e, (uint)tex.x, (uint)m1.z, flags);
            o.extra = float4(-TransformWorldToView(world).z, (flags & F_SOFT) ? m1.y : 0, m1.x > 1.5 ? 1 : 0, custom);
            return o;
        }

        Varyings Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID) { return Expand(vid, iid, false); }
        Varyings VertOverdraw(uint vid : SV_VertexID, uint iid : SV_InstanceID) { return Expand(vid, iid, true); }

        float4 Frag(Varyings i) : SV_Target
        {
            uint e = i.info.x;
            float4 c = SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, i.uv.xy, i.info.y);
            float second = EM(e, S_MAT1).w;
            if (second > 0.5)
            {
                // Sword slashes: the shape texture times a second texture over the mesh's UVs, and a
                // scrolling noise mask (observed inputs; the vendor graph's exact mix isn't reproduced).
                c *= SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, i.uv.xy, second - 1);
                if (i.info.w & F_NOISETEX) c.a *= saturate(SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, i.uv.zw, i.info.z).r * 2);
            }
            else if (i.info.w & F_NOISETEX) c *= SAMPLE_TEXTURE2D_ARRAY(_Textures, sampler_Textures, i.uv.zw, i.info.z);
            c *= i.color;
            if (i.extra.y > 0)
            {
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                c.a *= saturate((scene - i.extra.x) * i.extra.y);
            }
            c.a = saturate(c.a);
            uint blend = (uint)EM(e, S_MAT1).x;
            // Premultiplied materials (One, OneMinusSrcAlpha) carry alpha in their colour already.
            float3 rgb = i.extra.z > 0.5 ? c.rgb * i.color.a : c.rgb * c.a;
            return float4(rgb, blend == 0 ? 0 : c.a);
        }

        float4 FragOverdraw(Varyings i) : SV_Target { return float4(0, 1, 0, 0); }
        ENDHLSL

        Pass
        {
            Name "EffectParticles"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        Pass
        {
            Name "EffectOverdraw"
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One One
            ColorMask G
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertOverdraw
            #pragma fragment FragOverdraw
            ENDHLSL
        }
    }
}
