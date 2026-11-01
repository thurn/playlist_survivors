// The dark-core-edges monster look's bodies (neon-look.md, The Axes > Monsters; EdgeLook.cs): the
// crowd's style mesh skinned as every look skins it (MonsterCommon.hlsl), drawn as near-black glass
// lit only by the type's palette hue in its rim, its feature edges and its inner core
// (MonsterEdge.hlsl). As an impulse ring passes (Impulse.hlsl's ImpulseCrowd) a living monster hops
// and its rim and lines take the ring's light. Opaque, but in the transparent queue where the
// crystal draws: the renderers force depth priming, and outside the opaque queue the crowd needs
// no depth prepass of its own.
// Under the global _GameIdMask (the stills' ID render) it draws flat white.
Shader "Look/MonsterEdge"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.1, 0.8, 1.0, 1)
        _Core ("Glass (void.floor, linear)", Color) = (0.002, 0.002, 0.004, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Rim ("Rim gain", Float) = 2.0
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Edge gain", Float) = 2.6
        _EdgePx ("Edge width (px)", Float) = 1.25
        _Beat ("Edge lift on the beat", Float) = 0.6
        _Ring ("Ring light on rim and lines", Float) = 1.5
        _CoreGlow ("Core at rest", Float) = 0.1
        _CoreFlare ("Core on the beat", Float) = 0.8
        _CoreSize ("Core radius (share of the body's)", Float) = 0.18
        _Gain ("Horde gain (unused)", Float) = 1
        _Peak ("Horde peak (unused)", Float) = 1
        _Facets ("Faceting (unused)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Core; half4 _Flash;
            half _Rim; half _RimPower; half _Edge; half _EdgePx; half _Beat; half _Ring;
            half _CoreGlow; half _CoreFlare; half _CoreSize;
            half _Gain; half _Peak; half _Facets;
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterEdge.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y the beat's pulse (living only), zw the LOD fade
                nointerpolation float3 tint : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float4 bary : TEXCOORD5;
                nointerpolation float3 edges : TEXCOORD6;
                nointerpolation float4 core : TEXCOORD7; // the body's bounding sphere (CrowdSphere): centre (world), radius
                nointerpolation float3 ring : TEXCOORD8; // the impulse rings' light where it stands (linear)
            };

            Varyings vert(MonsterAttributes v)
            {
                MonsterVertex c = MonsterSkin(v);
                MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
                float height;
                Varyings o;
                float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
                float hop;
                ImpulseCrowd(m.position.xz, hop, o.ring);
                o.ring *= alive;
                c.positionWS.y += hop * alive;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.fx.x, alive * max(_SL_Pulse, c.fx.y), c.fx.zw);
                o.tint = _Tint.rgb; // the slot's hue, not the role's gem
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                o.edges = v.edges;
                o.core = CrowdSphere(m, height);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float lit, fres;
                float3 color = EdgeSurface(i.positionWS, i.normalWS, i.bary, i.edges, i.tint, i.fx.y, 0, lit, fres);
                float3 core = EdgeCore(i.positionWS, i.core, i.tint, i.fx.y);
                color += core;
                color += i.ring * _Ring * (lit + fres);
                // A hit lights what is lit already toward white: the lines, the rim and the core.
                color += _Flash.rgb * i.fx.x * (2.0 * lit + fres + 0.5 * Luminance(core));
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings vert(MonsterAttributes v)
            {
                ShadowVaryings o;
                // URP repeats the shadow draw for each cascade: skip a monster outside this cascade's
                // light-space box (the depth axis is not tested), as MonsterCrystal does.
                uint slot = CrowdSlot(v.instanceID);
                float height;
                float4 sphere = CrowdSphere(_Instances[slot], height);
                float4 cs = mul(UNITY_MATRIX_VP, float4(sphere.xyz, 1));
                float2 extent = sphere.w * float2(length(UNITY_MATRIX_VP[0].xyz), length(UNITY_MATRIX_VP[1].xyz));
                if (any(abs(cs.xy) - extent > cs.w))
                {
                    o.positionCS = float4(2, 2, 2, 1);
                    return o;
                }
                MonsterVertex c = MonsterSkin(v);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - c.positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(c.positionWS, c.normalWS, lightDirectionWS)));
                return o;
            }

            half4 frag(ShadowVaryings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
