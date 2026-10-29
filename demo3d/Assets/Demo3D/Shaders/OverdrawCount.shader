// The overdraw counters' debug replacement render (benchmarks.md, Measurements), drawn by
// OverdrawCounter into a float target with additive blending:
//   pass 0 counts every opaque fragment, no depth test (opaque depth complexity, red);
//   pass 1 lays the opaque depth;
//   pass 2 counts transparent fragments that pass that depth (green).
Shader "Hidden/Demo3D/OverdrawCount"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        float4x4 _OverdrawVP;

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
        };

        Varyings Vert(Attributes a)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(a);
            float3 world = TransformObjectToWorld(a.positionOS.xyz);
            o.positionCS = mul(_OverdrawVP, float4(world, 1));
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "CountOpaque"
            ZTest Always
            ZWrite Off
            Cull Back
            Blend One One
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            float4 Frag(Varyings i) : SV_Target { return float4(1, 0, 0, 0); }
            ENDHLSL
        }

        Pass
        {
            Name "OpaqueDepth"
            ZTest LEqual
            ZWrite On
            Cull Back
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            float4 Frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "CountTransparent"
            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend One One
            ColorMask G
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            float4 Frag(Varyings i) : SV_Target { return float4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}
