// The wireframe hologram monster look's bodies (neon-look.md, The Axes > Monsters; HologramLook.cs):
// the crowd's style mesh skinned as every look skins it (MonsterCommon.hlsl), drawn as light added
// over the world: lines in the type's palette hue, a thin rim and a faint fill (MonsterHologram.hlsl).
// The first pass writes depth only, so the second adds each draw's front surfaces alone (the same
// vertex function in both, so their depths match exactly). As an impulse ring passes (Impulse.hlsl's
// ImpulseCrowd) a living monster hops and its lines and rim take the ring's light. In the transparent
// queue where the crystal draws (outside the renderers' forced depth priming); casts no shadow, being
// light. Under the global _GameIdMask (the stills' ID render) it draws flat white.
Shader "Look/MonsterHologram"
{
    Properties
    {
        [HDR] _Tint ("Slot hue (linear)", Color) = (0.1, 0.8, 1.0, 1)
        [HDR] _Flash ("Flash (shatter.core, linear)", Color) = (1, 1, 1, 1)
        _AllEdges ("Every triangle edge a line", Float) = 0
        _Fill ("Fill face-on", Float) = 0.03
        _FillRim ("Fill toward the silhouette", Float) = 0.08
        _Veil ("Fill opacity: how much it dims what is behind", Float) = 0.25
        _Rim ("Rim gain", Float) = 1.6
        _RimPower ("Rim falloff", Float) = 4.0
        _Edge ("Line gain", Float) = 2.4
        _EdgePx ("Line width (px)", Float) = 1.25
        _Beat ("Line lift on the beat", Float) = 0.3
        _Sweep ("Beat band gain", Float) = 0.5
        _SweepWidth ("Beat band half-width (share of height)", Float) = 0.08
        _Ring ("Ring light on lines and rim", Float) = 1.1
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
            half4 _Tint; half4 _Flash;
            half _AllEdges; half _Fill; half _FillRim; half _Veil; half _Rim; half _RimPower;
            half _Edge; half _EdgePx; half _Beat; half _Sweep; half _SweepWidth; half _Ring;
            half _Gain; half _Peak; half _Facets; // MonsterCommon.hlsl's, unused here
        CBUFFER_END
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterCommon.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Crowd/Shaders/MonsterHologram.hlsl"
        #include "Packages/com.playlistsurvivors.look/Runtime/Impulse/Shaders/Impulse.hlsl"

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            nointerpolation float4 fx : TEXCOORD2; // x hit, y the beat's pulse (living only), zw the LOD fade
            float fogFactor : TEXCOORD3;
            float4 bary : TEXCOORD4;
            nointerpolation float3 edges : TEXCOORD5;
            nointerpolation float3 ring : TEXCOORD6; // the impulse rings' light where it stands (linear)
            float h01 : TEXCOORD7;                    // height over the body, 0 feet to 1 head
        };

        Varyings vert(MonsterAttributes v)
        {
            MonsterVertex c = MonsterSkin(v);
            MonsterInstance m = _Instances[CrowdSlot(v.instanceID)];
            Varyings o;
            float alive = (m.flags & CROWD_ALIVE) != 0 ? 1 : 0;
            float hop, height;
            ImpulseCrowd(m.position.xz, hop, o.ring);
            o.ring *= alive;
            CrowdSphere(m, height);
            o.h01 = (c.positionWS.y - m.position.y) / max(height, 1e-3);
            c.positionWS.y += hop * alive;
            o.positionWS = c.positionWS;
            o.positionCS = TransformWorldToHClip(c.positionWS);
            o.normalWS = c.normalWS;
            o.fx = float4(c.fx.x, alive * max(_SL_Pulse, c.fx.y), c.fx.zw);
            o.fogFactor = ComputeFogFactor(o.positionCS.z);
            o.bary = v.bary;
            o.edges = v.edges;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "HologramDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            half4 frag(Varyings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_GameIdMask > 0.5) return half4(1, 1, 1, 1);
                float lit, fres;
                float3 color = HologramSurface(i.positionWS, i.normalWS, i.bary, i.edges, _Tint.rgb, i.fx.y, 0, lit, fres);
                color += _Tint.rgb * HologramSweep(i.h01, i.fx.y);
                color += i.ring * _Ring * (lit + fres);
                // A hit lights what is lit already toward white: the lines and the rim.
                color += _Flash.rgb * i.fx.x * LookFlash(LOOK_HIT_CALM) * (1.2 * lit + fres + 0.1);
                // Light fades into the fog rather than taking its colour: it is added to what is behind.
                // The fill is a faint veil over what is behind (premultiplied: its light added, the world dimmed
                // by _Veil), so a near monster's body sets it off from the one behind it. Both fade into the
                // fog, which takes neither's colour.
                return half4(MixFogColor(color, 0, i.fogFactor), MixFogColor(_Veil.xxx, 0, i.fogFactor).x);
            }
            ENDHLSL
        }
    }
}
