// The reactive grid floor (neon-look.md, The Axes > World and The impulse field; neon-look-style.md, World): the void
// floor in the palette's void.floor with an emissive grid of grid.line, every fourth line about twice as bright, that
// bends and brightens as the impulse field's rings pass (Impulse.hlsl): the lines are shoved outward from each ring's
// centre (Geometry Wars' warping grid), the floor lifts under the front, and the lines take grid.pulse and the ring's
// colour. On the beat the lines (only the lines) breathe up a little (the beat globals' pulses). Lines thinner than a
// pixel dim rather than widen, and fade out where a cell is under a few pixels, so the far floor has no moiré. It
// takes no scene fog: the arenas' night fog would lift the void (the world kit fogs into void.fog). A zone tint
// replaces the lines' hue keeping their brightness. Under the global _GameIdMask (the stills' ID render) it draws black.
Shader "PS/Look/GridFloor"
{
    Properties
    {
        _Cell ("Cell (m)", Float) = 1.5
        _Major ("Cells per major line", Float) = 4
        _LineGain ("Line brightness (1: grid.line)", Float) = 1
        _Breathe ("Beat breath", Float) = 0.35
        _RingGain ("Ring light on lines", Float) = 2.2
        _RingFill ("Ring light on the floor", Float) = 0.05
        _PushM ("Warp: lines shoved per unit strength (m)", Float) = 0.9
        _LiftM ("Lift under a front per unit strength (m)", Float) = 0.12
        _Tint ("Zone tint (rgb) and how much it replaces the lines' hue (a)", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Cell, _Major, _LineGain, _Breathe, _RingGain, _RingFill, _PushM, _LiftM;
            half4 _Tint;
        CBUFFER_END

        float3 FloorLifted(float3 positionOS)
        {
            float3 ws = TransformObjectToWorld(positionOS);
            float strength;
            float3 light;
            float2 push;
            ImpulseAt(ws.xz, strength, light, push);
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

            half4 _LookVoidFloor, _LookGridLine, _LookGridPulse;
            float _SL_Pulse, _SL_BarPulse;
            float _GameIdMask;

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
                c *= saturate(2 - 8 * px / cell);                    // a cell under ~4 px fades: no moiré
                return max(c.x, c.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_GameIdMask > 0.5) return half4(0, 0, 0, 1);
                float strength;
                float3 light;
                float2 push;
                ImpulseAt(i.positionWS.xz, strength, light, push);
                // The warp: a line drawn here is the one shoved here from nearer the ring's centre.
                float2 p = i.positionWS.xz - push * _PushM;
                float minor = GridLine(p, _Cell, 0.012);
                float major = GridLine(p, _Cell * _Major, 0.025);
                float lines = max(0.4 * minor, major);

                float3 hue = _LookGridLine.rgb;
                float lum = dot(hue, float3(0.2126, 0.7152, 0.0722));
                float tintLum = max(1e-4, dot(_Tint.rgb, float3(0.2126, 0.7152, 0.0722)));
                hue = lerp(hue, _Tint.rgb * (lum / tintLum), _Tint.a);
                float breath = _Breathe * saturate(0.6 * _SL_Pulse + 0.4 * _SL_BarPulse);
                float3 lineColour = lerp(hue * _LineGain, _LookGridPulse.rgb, breath)
                    + _RingGain * (_LookGridPulse.rgb * saturate(strength) + light);
                float3 colour = _LookVoidFloor.rgb + lines * lineColour + _RingFill * light;
                return half4(colour, 1);
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformWorldToHClip(FloorLifted(positionOS.xyz)); }
            half4 frag() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
