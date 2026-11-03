Shader "Champions/RangedFacet"
{
    // Each flight placed and edge-lit from PieceBatch's buffer by its instance.
    Properties { _Body("Body", Color)=(0.006,0.008,0.014,1) _ChampionsId("ID", Float)=252 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            #include "Assets/Champions/Render/Effects/Shaders/Pieces.hlsl"
            struct A { float3 p:POSITION; float3 bary:TEXCOORD1; uint instance:SV_InstanceID; };
            struct V { float4 p:SV_POSITION; float3 bary:TEXCOORD0; nointerpolation half3 edge:TEXCOORD1; };
            half4 _Body; float _ChampionsId;
            V vert(A a)
            {
                Piece piece = PieceOf(a.instance);
                V o; o.p=TransformWorldToHClip(mul(piece.objectToWorld, float4(a.p, 1)).xyz); o.bary=a.bary; o.edge=piece.colour.rgb; return o;
            }
            half4 frag(V i):SV_Target
            {
                float3 d=max(fwidth(i.bary), 0.00001);
                float3 aa=smoothstep(d*0.35,d*1.25,i.bary);
                float edgeLight=1-min(aa.x,min(aa.y,aa.z));
                CHAMPIONS_ID_PASS(_ChampionsId);
                return half4(lerp(_Body.rgb,i.edge,edgeLight),1);
            }
            ENDHLSL
        }
    }
}
