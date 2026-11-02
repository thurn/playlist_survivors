// The hero in the edge look (design.md, The Hero, The Look > The reference: the trailer; plan.md, Rendering > The edge
// look): the frozen look package's HeroEdge.shader and HeroLook.hlsl as the neon edge trailer drew the knight with them
// (git 138684f2, its 1.4 px lines), ported by review onto the crowd's skinning (MonsterCommon.hlsl), the palette posed on
// the CPU (MonsterFigure). The monsters' grammar on the hero light rung: smoked glass a little lit in hero.body, a strong
// fresnel rim, lines on the knight's feature edges in hero.body, and a core of light in its chest seen through the glass,
// so the small figure is a bright cut-crystal knight among the swarm's coloured outlines. The crystal blade it holds is
// the same glass with every facet edged in hero.blade, its spine lit and its blow flare kept; where the trailer drew it
// with a second material, its vertices carry it here (TEXCOORD0: spine, along, 1 on the blade). What changed from the
// trailer's: the world's beat wave stands in for its beat globals (as the monsters'); the hurt colour comes as the
// instance's tint and the impact's and a blink's flare as its flare (a blow flares the blade as the trailer's did, and the
// knight's lines); the trailer's demo3d set no scene exposure, so its hero level, exp2(0.8) over it, is a constant; the
// stills' type-ID pass for its mask; no shadow caster, motion vectors or fog; opaque in the geometry queue, as the
// monsters. --look trailer draws with exactly this and HeroGains.Trailer (MonsterLookTests pins both).
Shader "Champions/HeroEdge"
{
    Properties
    {
        [HDR] _Tint ("hero.body (linear): the knight's light when the instance has no tint", Color) = (1, 0.97, 0.92, 1)
        [HDR] _BladeTint ("hero.blade (linear)", Color) = (1, 1, 1, 1)
        _EdgePx ("Line width (px at 2160 rows, the trailer's capture)", Float) = 1.4
        _Glow ("Blade spine light", Float) = 0.6
        _Glass ("Glass light", Float) = 0.03
        _Rim ("Rim gain", Float) = 0.6
        _RimPower ("Rim falloff", Float) = 3.0
        _Edge ("Line gain", Float) = 1.0
        _Beat ("Line and core lift on the beat", Float) = 0.15
        _CoreGlow ("Chest core gain", Float) = 0.35
        _CoreSize ("Chest core radius (m)", Float) = 0.22
        _Springs ("Motion springs (unused)", Float) = 0
        _Swarm ("Motion swarm (unused)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _BladeTint;
            half _EdgePx; half _Glow; half _Glass; half _Rim; half _RimPower; half _Edge; half _Beat;
            half _CoreGlow; half _CoreSize;
            half _Springs; half _Swarm;
        CBUFFER_END
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Assets/Champions/Render/Crowd/Shaders/MonsterLines.hlsl"

        // The trailer's hero level: exp2(HERO_TUNED_EXPOSURE 0.8) over its scene exposure's factor, which demo3d left at 1.
        #define HERO_LEVEL 1.7411011
        // The chest's core: a point this high over the feet (the knight's only).
        #define HERO_CHEST_M 1.15
        // HeroPresenter's impact flare: a blow's frame flares the blade fully from it.
        #define HERO_IMPACT_FLARE 0.5
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

            // The hero's style mesh: the monsters' attributes and the blade's (StyleMesh.Unweld keeps UV0).
            struct HeroAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 blade : TEXCOORD0;  // x 1 on the blade's spine, 0 on its edges; y 0 at the guard, 1 at the tip; z 1 on the blade
                float4 boneIndex : TEXCOORD1;
                float4 boneWeight : TEXCOORD2;
                float4 bary : TEXCOORD3;
                float3 edges : TEXCOORD5;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 blade : TEXCOORD2;
                float4 bary : TEXCOORD3;
                nointerpolation float3 edges : TEXCOORD4;
                nointerpolation float3 body : TEXCOORD5;   // the knight's light (the instance's tint: white, or hurt)
                nointerpolation float4 chest : TEXCOORD6;  // the chest's core (world), and the flare
            };

            Varyings vert(HeroAttributes v)
            {
                MonsterAttributes a;
                a.positionOS = v.positionOS;
                a.normalOS = v.normalOS;
                a.boneIndex = v.boneIndex;
                a.boneWeight = v.boneWeight;
                a.bary = v.bary;
                a.edges = v.edges;
                a.instanceID = v.instanceID;
                MonsterVertex c = MonsterSkin(a);
                MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.blade = v.blade;
                o.bary = v.bary;
                o.edges = max(v.edges, v.blade.z); // every edge of the blade is a line
                o.body = c.tint;
                o.chest = float4(m.position + float3(0, HERO_CHEST_M * m.scale, 0), c.fx.y);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 facet = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                if (dot(facet, V) < 0) facet = -facet;
                float3 N = normalize(lerp(normalize(i.normalWS), facet, 0.6));
                float nv = saturate(dot(N, V));
                // The beat on the hero: the world's beat wave leaving its feet (the trailer's SL_Wave), a lift, never a flash.
                float beat = BeatWaveAt(i.positionWS.xz);
                float blade = i.blade.z, flare = i.chest.w;
                float3 body = lerp(i.body, _BladeTint.rgb, blade);
                // The glass: each facet a shade apart (the triangle's random), lit a little, never black.
                float3 color = body * _Glass * (0.6 + 0.8 * i.bary.w);
                color += body * _Rim * pow(1 - nv, _RimPower);
                float lit = EdgeLine(i.bary.xyz, i.edges, _EdgePx);
                color += body * _Edge * lit * (0.75 + 0.25 * (1 - nv)) * (1 + _Beat * beat) * (1 + flare * (1 - blade));
                // The chest's core: where the view ray passes near it.
                float3 ray = normalize(i.positionWS - _WorldSpaceCameraPos);
                float d = length(cross(i.chest.xyz - _WorldSpaceCameraPos, ray));
                float r = max(_CoreSize, 1e-3);
                color += body * exp(-(d * d) / (r * r)) * _CoreGlow * (1 + _Beat * beat) * (1 - blade);
                // The blade's own light: a line down the spine fading toward the tip, up on each beat, and the blow's flare
                // over the whole blade past white.
                float spine = smoothstep(0.55, 1.0, i.blade.x);
                float bladeFlare = saturate(flare / HERO_IMPACT_FLARE);
                float glow = _Glow * spine * (1.0 - 0.4 * i.blade.y) * (0.75 + 0.5 * beat) + bladeFlare * (0.35 + 0.65 * spine);
                color += _BladeTint.rgb * 2.0 * glow * blade;
                return half4(color * HERO_LEVEL, 1);
            }
            ENDHLSL
        }
    }
}
