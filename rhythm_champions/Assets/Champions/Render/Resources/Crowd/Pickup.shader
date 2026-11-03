Shader "Champions/Pickup"
{
    Properties { _Motion ("Active seconds", Float) = 0 _ChampionsId ("ID", Float) = 252 }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            float _Motion, _ChampionsId;
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; float phase:TEXCOORD1; };
            V vert(A v) { V o; o.p=TransformObjectToHClip(v.p.xyz); o.uv=v.uv;
                o.phase=unity_ObjectToWorld._m03*1.3+unity_ObjectToWorld._m23*.7; return o; }
            half4 frag(V i):SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                float flow=.5+.5*sin(i.uv.y*15-_Motion*4+i.phase);
                float core=1-saturate(length(i.uv-.5)*1.5);
                return half4(lerp(float3(.015,.42,.045),float3(.06,1.1,.20), .25+.35*flow+.3*core),1);
            }
            ENDHLSL
        }
    }
}
