Shader "Champions/MeleeEdge"
{
    Properties { _Light ("White edge", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Light;
                float _ChampionsId;
            CBUFFER_END
            struct A { float3 p:POSITION; float4 bary:TEXCOORD3; float3 edges:TEXCOORD5; };
            struct V { float4 p:SV_POSITION; float3 bary:TEXCOORD0; nointerpolation float3 edges:TEXCOORD1; };
            V vert(A a) { V o; o.p=TransformObjectToHClip(a.p); o.bary=a.bary.xyz; o.edges=a.edges; return o; }
            half4 frag(V i):SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                float3 d=i.bary/max(fwidth(i.bary),1e-5);
                float3 e=(1-smoothstep(.35,1.85,d))*i.edges;
                float edge=max(e.x,max(e.y,e.z));
                return half4(lerp(half3(.0024,.0027,.004),_Light.rgb,edge),1);
            }
            ENDHLSL
        }
    }
}
