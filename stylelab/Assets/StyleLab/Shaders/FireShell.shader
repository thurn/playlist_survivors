// Treatment 3, "Living flame", the flames: a second copy of the mesh pushed out along its
// normals, its upper faces stretched up into licking tongues, drawn additively in flowing
// noise with a black-to-white-hot ramp. Brighter as the beat wave passes.
Shader "StyleLab/FireShell"
{
    Properties
    {
        [HDR] _Ember ("Ember", Color) = (0.6, 0.05, 0.0, 1)
        [HDR] _Flame ("Flame", Color) = (3.0, 0.8, 0.08, 1)
        [HDR] _Core ("Core", Color) = (6.0, 4.5, 2.2, 1)
        _Thickness ("Thickness (metres)", Float) = 0.07
        _Lick ("Tongue height (metres)", Float) = 0.35
        _Intensity ("Intensity", Float) = 0.7
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
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
            #include "StyleLabCommon.hlsl"
            #include "FireCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Ember; half4 _Flame; half4 _Core; float _Thickness; float _Lick; half _Intensity;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float up = saturate(n.y * 0.6 + 0.4);
                float nz = SL_Noise(v.rest.xyz * 2.5 + float3(0, -_SL_Time * 2.8, 0) + _Seed * 9.0);
                float grow = 1.0 + _Death * 1.5; // flares up as it burns away
                p += n * _Thickness * (0.35 + nz) * grow;
                p.y += _Lick * nz * nz * up * (1.0 + _SL_Pulse * 0.7) * grow;
                return SL_Finish(v, p, n);
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float edge = 1.0 - abs(dot(N, V));
                float f = SL_Fbm(i.rest.xyz * 3.2 + float3(0, -_SL_Time * 3.6, 0) + _Seed * 7.0);
                float h01 = saturate(i.rest.y / max(_HeightM, 0.1));
                float wave = SL_Wave(i.positionWS, 6.0);
                float flame = saturate(f * 1.9 - 0.4 + (1.0 - h01) * 0.2) * (0.45 + edge * 1.1);
                flame *= (0.8 + 0.7 * wave) * (1.0 - smoothstep(0.7, 1.0, _Death));
                float3 color = FireRamp(flame, _Ember.rgb, _Flame.rgb, _Core.rgb) * _Intensity;
                return half4(color, 0);
            }
            ENDHLSL
        }
    }
}
