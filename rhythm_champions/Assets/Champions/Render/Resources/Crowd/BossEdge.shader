// The Boss Rush bosses' shared look (boss-rush.md, Look, effects, shaders and sound; ps-8z4.3): MonsterEdge's near-black
// glass, rim, feature lines and inner core exactly (MonsterCommon.hlsl, MonsterEdge.hlsl), plus six variations a boss
// turns up by float (BossLook.cs), each off at 0:
//   _Scan     a hologram: bright scanlines climbing the body, the glass between them thinning (the Mirror);
//   _Crystal  crystal facets: facets glint in turn as the view crosses them (the Rime Golem);
//   _Flow     light flowing along the edges: a bright band sweeping the lines (the Magma Heart, the Watcher);
//   _Glitch   a glitch offset: slices of the body jump sideways for a frame or two (the Signal's bosses);
//   _Ghost    a ghostly fade: the body dissolves in a dither that breathes (the Wraith Council);
//   _Ember    an ember core: the core and the silhouette's lines run hot toward _EmberTint (the Magma Heart, the Demon Lord).
// Floats, not keywords: one boss is on screen at a time, so the few extra instructions cost nothing measurable, and the
// shader adds the same variants as MonsterEdge (none per variation) to the player's budget. The colours stay the
// boss's: _EmberTint is its flavour hue's hot end, never the danger orange/red, yellow or white (Decision 6).
Shader "Champions/BossEdge"
{
    Properties
    {
        [HDR] _Tint ("Hue (linear, levelled)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (void.body, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 2.6
        _EdgePx ("Edge width (px at 2160 rows)", Float) = 1.25
        _Beat ("Edge lift on the beat", Float) = 0.3
        _Ring ("Ring light on rim and lines", Float) = 1.1
        _CoreGlow ("Core at rest", Float) = 0.1
        _CoreFlare ("Core on the beat", Float) = 0.35
        _CoreSize ("Core radius (share of the body's)", Float) = 0.18
        _Springs ("Motion springs (0 or 1)", Float) = 1
        _Swarm ("Motion swarm (0 or 1)", Float) = 1
        _Scan ("Hologram scanlines", Float) = 0
        _ScanDensity ("Scanlines per metre", Float) = 6
        _ScanSpeed ("Scanline climb (m/s)", Float) = 1.2
        _Crystal ("Crystal facet glints", Float) = 0
        _Flow ("Light flowing along the edges", Float) = 0
        _FlowSpeed ("Flow speed (bands/s)", Float) = 0.8
        _FlowScale ("Flow bands per metre", Float) = 0.35
        _Glitch ("Glitch offset (m)", Float) = 0
        _Ghost ("Ghost fade (0 solid to 1 gone)", Float) = 0
        _Ember ("Ember core", Float) = 0
        [HDR] _EmberTint ("Ember hot end (linear)", Color) = (1, 0.35, 0.1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Core; half4 _Flash;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat; half _Ring;
            half _CoreGlow; half _CoreFlare; half _CoreSize;
            half _Springs; half _Swarm;
            half _Scan; half _ScanDensity; half _ScanSpeed; half _Crystal;
            half _Flow; half _FlowSpeed; half _FlowScale; half _Glitch; half _Ghost; half _Ember;
            half4 _EmberTint;
        CBUFFER_END
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterEdge.hlsl"

        float BossHash(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }
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

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y the pulse (living only), zw the LOD fade
                nointerpolation float3 tint : TEXCOORD3;
                float4 bary : TEXCOORD5;
                nointerpolation float3 edges : TEXCOORD6;
                nointerpolation float4 core : TEXCOORD7; // the body's bounding sphere (CrowdSphere): centre (world), radius
                nointerpolation float3 ring : TEXCOORD8; // the event rings' light where it stands (linear)
            };

            Varyings vert(MonsterAttributes v)
            {
                MonsterVertex c = MonsterSkin(v);
                MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
                float height;
                Varyings o;
                float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
                float hop, pulse;
                MonsterWorld(m.position.xz, hop, o.ring, pulse);
                o.ring *= alive;
                c.positionWS.y += hop * alive;
                if (_Glitch > 0)
                {
                    // Slices a quarter metre tall jump sideways now and then, a few frames at a time.
                    float slice = floor(c.positionWS.y * 4), frame = floor(_Time.y * 12);
                    float jump = step(0.9, BossHash(float2(slice, frame)));
                    c.positionWS.x += (BossHash(float2(frame, slice)) * 2 - 1) * _Glitch * jump * alive;
                }
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.fx.x, alive * max(pulse, c.fx.y), c.fx.zw);
                o.tint = _Tint.rgb;
                o.bary = v.bary;
                o.edges = v.edges;
                o.core = CrowdSphere(m, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_Ghost > 0)
                {
                    // A screen-door dissolve that breathes with time, so the body thins without leaving the opaque queue.
                    float breathe = _Ghost * (0.75 + 0.25 * sin(_Time.y * 2.1));
                    clip(BossHash(i.positionCS.xy) - breathe);
                }
                CHAMPIONS_ID_PASS(_ChampionsId)
                float lit, fres;
                float3 color = EdgeSurface(i.positionWS, i.normalWS, i.bary, i.edges, i.tint, i.fx.y, 0, _Rim, lit, fres);
                float3 core = EdgeCore(i.positionWS, i.core, i.tint, i.fx.y);
                float t = _Time.y;
                if (_Scan > 0)
                {
                    float band = frac(i.positionWS.y * _ScanDensity - t * _ScanSpeed * _ScanDensity);
                    float scanline = smoothstep(0.85, 1, band);
                    color *= lerp(1, 0.55 + 0.45 * scanline, saturate(_Scan));
                    color += i.tint * _Scan * scanline * (0.35 + fres);
                }
                if (_Crystal > 0)
                {
                    // Each facet (bary.w, its triangle's random) glints in turn, brighter toward the rim.
                    float glint = smoothstep(0.92, 1, frac(i.bary.w * 7.3 + t * 0.45));
                    color += lerp(i.tint, float3(1, 1, 1), 0.25) * _Crystal * glint * (0.3 + fres);
                }
                if (_Flow > 0)
                {
                    float wave = frac(dot(i.positionWS, float3(0.3, 1, 0.2)) * _FlowScale - t * _FlowSpeed);
                    color += i.tint * _Edge * lit * _Flow * smoothstep(0.75, 1, wave) * 1.5;
                }
                if (_Ember > 0)
                {
                    float flicker = 0.85 + 0.15 * sin(t * 3.7 + i.core.x);
                    core = lerp(core, core * 2.5 * _EmberTint.rgb / max(Luminance(i.tint), 0.05), saturate(_Ember)) * flicker;
                    color += _EmberTint.rgb * _Ember * lit * fres * 1.5;
                }
                color += core;
                color += i.ring * _Ring * (lit + fres);
                color += _Flash.rgb * i.fx.x * LookFlash(LOOK_HIT_CALM) * (2.0 * lit + fres + 0.5 * Luminance(core));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
