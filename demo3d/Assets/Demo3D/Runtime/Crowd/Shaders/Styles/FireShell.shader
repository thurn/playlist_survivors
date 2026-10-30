// `--monster-style fire`, the flames (crowd.md, Monster styles): stylelab's FireShell, drawn as
// the type's shell (MonsterStyle.Shells) on the same instances. A second copy of the mesh pushed
// out along its normals, its upper faces stretched up into licking tongues, drawn additively in
// flowing noise with a black-to-white-hot ramp, brighter as the beat wave passes and flaring as
// the body burns away. Being additive light it casts no shadow, writes no depth and no motion
// vectors: under TAA it reprojects with what is behind it (crowd.md).
Shader "Demo3D/Styles/Shells/FireShell"
{
    Properties
    {
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _Thickness ("Thickness (metres)", Float) = 0.07
        _Lick ("Tongue height (metres)", Float) = 0.35
        _Intensity ("Intensity", Float) = 0.7
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Ember; half4 _Flame; half4 _Core; float _Thickness; float _Lick; half _Intensity;
            half _Gain;
            float _HeightM;
        CBUFFER_END

        #include "StyleCommon.hlsl"
        #include "FireCommon.hlsl"
        ENDHLSL

        Pass
        {
            Name "Flames"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            SLVaryings vert(SLAttributes v)
            {
                SLVertex c = SL_Skin(v);
                float3 p = c.positionWS, n = c.normalWS;
                float death = c.m.dissolve;
                float up = saturate(n.y * 0.6 + 0.4);
                float nz = SL_Noise(v.positionOS * 2.5 + float3(0, -_SL_Time * 2.8, 0) + c.center.w * 9.0);
                float grow = 1.0 + death * 1.5; // flares up as it burns away
                p += n * _Thickness * (0.35 + nz) * grow;
                p.y += _Lick * nz * nz * up * (1.0 + _SL_Pulse * 0.7) * grow;
                return SL_Finish(v, c, p, n);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                CrowdLodClip(i.fx.zw, i.positionCS);
                if (_Demo3DIdMask > 0.5) return 0; // TD15's ID render: the body alone
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float edge = 1.0 - abs(dot(N, V));
                float f = SL_Fbm(i.rest * 3.2 + float3(0, -_SL_Time * 3.6, 0) + SL_SEED(i) * 7.0);
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                float wave = SL_Wave(i.positionWS, 6.0);
                float flame = saturate(f * 1.9 - 0.4 + (1.0 - h01) * 0.2) * (0.45 + edge * 1.1);
                flame *= (0.8 + 0.7 * wave) * (1.0 - smoothstep(0.7, 1.0, SL_DEATH(i)));
                float3 color = FireRamp(flame, _Ember.rgb, _Flame.rgb, _Core.rgb) * _Intensity;
                return half4(color * _Gain, 0);
            }
            ENDHLSL
        }
    }
}
