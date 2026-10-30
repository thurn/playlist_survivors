// Treatment 8, "Void": creatures cut out of the night sky. The body shows a starfield and a
// faint nebula fixed to the screen, so each monster looks like a hole into space, ringed by
// an intense coloured edge that flares as the beat wave passes. Death implodes it to a point.
Shader "StyleLab/Void"
{
    Properties
    {
        [HDR] _RimColor ("Edge", Color) = (0.6, 0.3, 3.0, 1)
        _RimPower ("Edge tightness", Float) = 4
        _NebulaA ("Nebula A", Color) = (0.25, 0.05, 0.4, 1)
        _NebulaB ("Nebula B", Color) = (0.0, 0.3, 0.45, 1)
        [HideInInspector] _Hit ("", Float) = 0
        [HideInInspector] _Death ("", Float) = 0
        [HideInInspector] _Seed ("", Float) = 0
        [HideInInspector] _Center ("", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HeightM ("", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "StyleLabCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor; half _RimPower; half4 _NebulaA; half4 _NebulaB;
                SL_INSTANCE_PROPS
            CBUFFER_END

            SLVaryings vert(SLAttributes v)
            {
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float m = smoothstep(0.0, 1.0, _Death);
                p = lerp(p, _Center.xyz + float3(0, _HeightM * 0.5, 0), m * m);
                return SL_Finish(v, p, n);
            }

            float Stars(float2 p, float scale, float seed)
            {
                float2 q = p * scale;
                float2 cell = floor(q);
                float2 f = frac(q) - 0.5;
                float h = SL_Hash(float3(cell, seed));
                float2 at = (SL_Hash3(float3(cell, seed + 1.0)).xy - 0.5) * 0.7;
                float size = 0.04 + 0.08 * SL_Hash(float3(cell, seed + 2.0));
                float twinkle = 0.6 + 0.4 * sin(_SL_Time * (2.0 + h * 5.0) + h * 40.0);
                return step(0.72, h) * smoothstep(size, 0.0, length(f - at)) * twinkle;
            }

            half4 frag(SLVaryings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float NdotV = saturate(dot(N, V));
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 nv = mul((float3x3)UNITY_MATRIX_V, N);
                float2 p = suv * float2(_ScreenParams.x / _ScreenParams.y, 1.0) + nv.xy * 0.02 + _Seed * 3.7;
                float wave = SL_Wave(i.positionWS, 6.0);

                float stars = Stars(p, 40.0, 1.0) + Stars(p, 90.0, 7.0) * 0.6 + Stars(p, 18.0, 3.0) * 1.5;
                float n = SL_Fbm(float3(p * 3.0, _SL_Time * 0.05));
                float3 nebula = lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(n * 1.6 - 0.3)) * pow(saturate(n), 2.2) * 0.9;
                float3 color = nebula + stars * (2.2 + wave * 2.5);

                float rim = pow(1.0 - NdotV, _RimPower);
                color += _RimColor.rgb * rim * (0.9 + 2.5 * wave + _Death * 6.0);
                color += _RimColor.rgb * smoothstep(0.78, 0.86, 1.0 - NdotV) * 0.35;
                color += float3(1.0, 1.0, 1.0) * _Hit * rim;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
        UsePass "Hidden/StyleLab/Passes/SHADOWCASTER"
        UsePass "Hidden/StyleLab/Passes/DEPTHONLY"
        UsePass "Hidden/StyleLab/Passes/DEPTHNORMALS"
    }
}
