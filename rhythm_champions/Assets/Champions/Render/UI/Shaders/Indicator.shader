// The hero's indicator (HeroIndicator.cs; design.md, The Dash > The indicator), drawn in the world over the hero's head on
// a quad that faces the camera, everything in the fragment from the quad's metres: a white strip 1.2 m wide, the next two
// beats' ticks sliding in from both ends to the centre, the dash's pip at the centre (a dim ring filling while it cools,
// solid when ready, a flash on an on-beat dash, a blink on a dropped press). Health lives in the HUD orb. Widths are in pixels (fwidth), so lines stay hairlines at any
// distance. Premultiplied over everything, never hidden by what stands in front. In the stills' type-ID pass it draws as one
// of the hero's effects where it covers.
Shader "Champions/Indicator"
{
    Properties
    {
        [HDR] _Body ("White (linear, hero.body)", Color) = (0.9, 0.94, 1, 1)
        [HDR] _Hurt ("Hurt (linear, hero.hurt)", Color) = (1, 0.05, 0.05, 1)
        _Ticks ("Ticks: x1, alpha1, x2, alpha2 (0 centre, 1 the ends)", Vector) = (0, 0, 0, 0)
        _Pip ("Pip: cooldown filled, ready, flash, blink", Vector) = (1, 1, 0, 0)
        _State ("Health, hurt flash, reserved, shown", Vector) = (1, 0, 0, 1)
        _ChampionsId ("Type-ID pass code", Float) = 252
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+100" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Body;
                half4 _Hurt;
                float4 _Ticks;
                float4 _Pip;
                float4 _State;
                float _ChampionsId;
            CBUFFER_END

            // Layout, metres (HeroIndicator.cs keeps the C# side's copy).
            static const float HalfWidth = 0.6;   // the strip
            static const float PipR = 0.07;
            static const float TickHalf = 0.06;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 m : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.m = v.positionOS.xy;
                return o;
            }

            // Coverage of a line wPx pixels wide at distance d (metres), px metres a pixel.
            float Line(float d, float wPx, float px) { return saturate(wPx * 0.5 - abs(d) / px + 0.5); }
            float Disc(float d, float px) { return saturate(-d / px + 0.5); }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.m;
                float px = max(length(fwidth(p)) * 0.7071, 1e-5);
                float ax = abs(p.x);
                float3 white = _Body.rgb;
                float a = 0;      // coverage
                float3 c = 0;     // premultiplied colour

                // The strip, cut round the pip.
                float strip = Line(length(float2(max(ax - HalfWidth, 0), p.y)), 1.5, px) * saturate((ax - PipR - 0.03) / px);
                a = max(a, strip * 0.4);
                c = max(c, white * strip * 0.4);

                // The ticks: each beat's pair converges on the centre, brighter as it nears, gone into the pip.
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float u = k == 0 ? _Ticks.x : _Ticks.z;
                    float alpha = k == 0 ? _Ticks.y : _Ticks.w;
                    float x = u * HalfWidth;
                    float t = Line(length(float2(ax - x, max(abs(p.y) - TickHalf, 0))), 2.0, px)
                              * alpha * saturate((x - PipR) / 0.02) * lerp(1.0, 0.35, u);
                    a = max(a, t);
                    c = max(c, white * t);
                }

                // The pip.
                float r = length(p);
                float blink = 1 - 0.85 * _Pip.w;
                float filled = frac(atan2(p.x, p.y) / (2 * PI) + 1) < _Pip.x ? 1 : 0;
                float ring = Line(r - PipR, 1.5, px);
                float pip = _Pip.y > 0.5 ? Disc(r - PipR, px) * 0.95 : max(ring * 0.22, Line(r - PipR, 3.0, px) * filled);
                pip *= blink;
                float halo = Line(r - PipR * (1 + 1.6 * (1 - _Pip.z)), 2.0, px) * _Pip.z;
                a = max(a, max(pip, halo));
                c = max(c, white * (pip * (1 + 2 * _Pip.z) + halo * 1.5));

                a *= _State.w;
                clip(a - 0.02);
                CHAMPIONS_ID_PASS(_ChampionsId)
                return half4(c * _State.w, a);
            }
            ENDHLSL
        }
    }
}
