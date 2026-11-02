// The endless grid (design.md, The World > The infinite arena, The Look > The beat wave, Zones; GridFloor.cs): the void
// floor with a quiet emissive grid in the zone key's hue, its lines at the bottom of ground light (_LineL, L* after the
// post), every fourth line the major one, the minor lines a share of it. As a beat ring passes (World.hlsl) the lines
// brighten toward _WaveL (a downbeat's front; below the top of ground light, L* 35, so the floor never rises out of it),
// the minor lines up to the major's light, and bend slightly outward (_WavePushM); the wake is gone within half a beat. Event rings past the knee (special
// moments) bend the whole grid harder, lift the floor under their front and light the lines in their colour. The zone
// key's hue crossfades behind the change's front. Lines thinner than a pixel dim rather than widen, fade with distance
// from the camera and fade out where a cell is under a few pixels, so the far floor is quiet and has no moire. The
// floor itself is void: no fill, no fog. Under the look metrics' ID render (_RC_IdPass) it draws black.
Shader "Champions/GridFloor"
{
    Properties
    {
        _Cell ("Cell (m)", Float) = 1.5
        _Major ("Cells per major line", Float) = 4
        _MinorHalfM ("Minor line half-width (m)", Float) = 0.01
        _MajorHalfM ("Major line half-width (m)", Float) = 0.02
        _MinorShare ("Minor lines' share of the major's light", Float) = 0.35
        _LineL ("Major lines' L* after the post", Float) = 12
        _WaveL ("Major lines' L* under a downbeat's front", Float) = 32
        _WavePushM ("Beat wave: lines shoved per unit strength (m)", Float) = 0.35
        _RingKnee ("An event ring's strength the floor ignores", Float) = 0.25
        _RingL ("Major lines' L* under a full event ring", Float) = 42
        _RingLight ("Event ring light on lines", Float) = 0.15
        _RingPushM ("Event rings: lines shoved per unit strength (m)", Float) = 0.7
        _LiftM ("Event rings: lift under a front per unit strength (m)", Float) = 0.12
        _FadeNear ("Lines fade from (m from the camera)", Float) = 30
        _FadeFar ("Lines faded by (m)", Float) = 90
        _FadeTo ("Faded lines' share", Float) = 0.35
        _ToneGain ("The tonemap's gain on dim light", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Assets/Champions/Render/World/Shaders/World.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Cell, _Major, _MinorHalfM, _MajorHalfM, _MinorShare, _LineL, _WaveL, _WavePushM, _RingKnee, _RingL, _RingLight,
                _RingPushM, _LiftM, _FadeNear, _FadeFar, _FadeTo, _ToneGain;
        CBUFFER_END

        float3 FloorLifted(float3 positionOS)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float strength;
            float3 light;
            float2 push;
            ImpulseAtKnee(ws.xz, _RingKnee, strength, light, push);
            ws.y += _LiftM * saturate(strength);
            return ws;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = FloorLifted(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // How much of a line of half-width halfW (m) covers this pixel, on a lattice of the given cell.
            float GridLine(float2 p, float cell, float halfW)
            {
                float2 d = abs(frac(p / cell + 0.5) - 0.5) * cell;   // m from the nearest line, per axis
                float2 px = max(fwidth(p), 1e-5);                    // m per pixel
                float2 w = max(halfW, 0.5 * px);                     // at least half a pixel wide
                float2 c = saturate(1 - (d - w) / px);
                c *= saturate(halfW / w);                            // thinner than a pixel: dimmer, not wider
                c *= saturate(2 - 8 * px / cell);                    // a cell under ~4 px fades: no moire
                return max(c.x, c.y);
            }

            // A line's colour before the post that lands at L* lStar after it: the hue at its brightest channel 1, scaled
            // so its luminance is the L*'s, through the tonemap's gain on dim light (neutral, about linear there).
            float3 Levelled(float3 hue, float lStar)
            {
                float3 h = hue / max(1e-4, max(hue.r, max(hue.g, hue.b)));
                float y = pow((lStar + 16) / 116, 3);
                return h * (y / max(1e-4, dot(h, float3(0.2126, 0.7152, 0.0722)) * _ToneGain));
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_RC_IdPass > 0.5) return half4(0, 0, 0, 1);
                float2 xz = i.positionWS.xz;
                float wave;
                float2 wavePush;
                BeatWave(xz, wave, wavePush);
                float ring;
                float3 light;
                float2 push;
                ImpulseAtKnee(xz, _RingKnee, ring, light, push);
                // The warp: a line drawn here is the one shoved here from nearer the rings' centres.
                float2 p = xz - wavePush * _WavePushM - push * _RingPushM;
                float minor = GridLine(p, _Cell, _MinorHalfM);
                float major = GridLine(p, _Cell * _Major, _MajorHalfM);
                float far = smoothstep(_FadeNear, _FadeFar, distance(i.positionWS, _WorldSpaceCameraPos));
                // Under the wave the minor lines come up to the major's light, so its front reads as a ring of light.
                float lines = max(lerp(_MinorShare, 1, wave) * minor, major) * lerp(1, _FadeTo, far);
                float lStar = lerp(lerp(_LineL, _WaveL, wave), _RingL, saturate(ring));
                float3 colour = Levelled(ZoneKey(xz), lStar) + _RingLight * light;
                return half4(lines * colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformWorldToHClip(FloorLifted(positionOS.xyz)); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
