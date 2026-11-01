// The solid-emissive monster look's bodies (neon-look.md, The Axes > Monsters; SolidLook.cs): the
// crowd's style mesh skinned as every look skins it (MonsterCommon.hlsl), drawn as a saturated
// self-lit body in the type's palette hue with a thin dark outline (MonsterSolid.hlsl): the body in
// URP's forward pass, the outline hull in the unlit pass URP draws beside it. Opaque, but in the
// transparent queue where the crystal draws: the renderers force depth priming, and outside the
// opaque queue the crowd needs no depth prepass of its own. Under the global _GameIdMask (the
// stills' ID render) the body draws flat white and the outline nothing, so the mask is the bodies.
Shader "Look/MonsterSolid"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.6, 0.2, 1.0, 1)
        _Outline ("Outline (void.floor, linear)", Color) = (0.002, 0.001, 0.003, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _Glow ("Emission gain", Float) = 1.0
        _Key ("Light from above", Range(0, 1)) = 0.55
        _Falloff ("Silhouette band darkness", Range(0, 1)) = 0.9
        _FalloffPx ("Silhouette band (px at 1080 rows)", Float) = 3
        _Facets ("Faceting", Range(0, 1)) = 0.5
        _HotSpot ("Hot spot on the beat", Float) = 0.9
        _Impulse ("Impulse light", Float) = 0.6
        _OutlinePx ("Outline width (px at 1080 rows)", Float) = 2.5
        _Gain ("Horde gain (unused)", Float) = 1
        _Peak ("Horde peak (unused)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; half4 _Outline; half4 _Flash;
            half _Glow; half _Key; half _Falloff; half _FalloffPx; half _Facets;
            half _HotSpot; half _Impulse; half _OutlinePx;
            half _Gain; half _Peak;
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterSolid.hlsl"

        // The skinned body, lifted by a passing ring: `light` is the ring's light on it, `pulse` the
        // beat's (living only), `centre` its bounding sphere's centre (CrowdSphere, lifted alike),
        // `radiusPx` its radius on screen (SolidRadiusPx).
        MonsterVertex SolidSkin(MonsterAttributes v, out float3 light, out float pulse, out float3 centre, out float radiusPx)
        {
            MonsterVertex c = MonsterSkin(v);
            MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
            float3 lift = c.positionWS;
            light = SolidImpulse(m.position, c.positionWS);
            pulse = (m.flags & CROWD_ALIVE) != 0 ? max(_SL_Pulse, c.fx.y) : 0;
            float height;
            float4 sphere = CrowdSphere(m, height);
            centre = sphere.xyz + (c.positionWS - lift);
            radiusPx = SolidRadiusPx(sphere);
            return c;
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                nointerpolation float4 fx : TEXCOORD2; // x hit, y the beat's pulse (living only), zw the LOD fade
                nointerpolation float radiusPx : TEXCOORD6; // the body's radius on screen
                nointerpolation float3 light : TEXCOORD3; // a passing ring's light
                float fogFactor : TEXCOORD4;
                float4 bary : TEXCOORD5;
            };

            Varyings vert(MonsterAttributes v)
            {
                float3 light, centre;
                float pulse, radiusPx;
                MonsterVertex c = SolidSkin(v, light, pulse, centre, radiusPx);
                Varyings o;
                o.positionWS = c.positionWS;
                o.positionCS = TransformWorldToHClip(c.positionWS);
                o.normalWS = c.normalWS;
                o.fx = float4(c.fx.x, pulse, c.fx.zw);
                o.radiusPx = radiusPx;
                o.light = light;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.bary = v.bary;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float3 color = SolidSurface(i.positionWS, i.normalWS, i.bary, _Tint.rgb, i.fx.y, 0, i.radiusPx);
                color += i.light;
                color = SolidHit(color, i.positionWS, i.normalWS, i.fx.x);
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation float2 lod : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(MonsterAttributes v)
            {
                float3 light, centre;
                float pulse, radiusPx;
                MonsterVertex c = SolidSkin(v, light, pulse, centre, radiusPx);
                Varyings o;
                o.positionCS = SolidOutlineCS(c.positionWS, c.normalWS, centre);
                o.lod = c.fx.zw;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.lod, i.positionCS);
                clip(0.5 - _GameIdMask);
                return half4(MixFog(_Outline.rgb, i.fogFactor), 1);
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
