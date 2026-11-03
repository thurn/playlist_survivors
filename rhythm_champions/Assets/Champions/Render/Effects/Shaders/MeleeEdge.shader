// The rigid weapon pieces' white feature edges over a dark body (the melee, magic and construct visuals'), each piece
// placed and lit from PieceBatch's buffer by its instance, so a frame's pieces of one mesh draw at once.
Shader "Champions/MeleeEdge"
{
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
            #include "Assets/Champions/Render/Effects/Shaders/Pieces.hlsl"
            float _ChampionsId;
            struct A { float3 p:POSITION; float4 bary:TEXCOORD3; float3 edges:TEXCOORD5; uint instance:SV_InstanceID; };
            struct V { float4 p:SV_POSITION; float3 bary:TEXCOORD0; nointerpolation float3 edges:TEXCOORD1; nointerpolation half3 light:TEXCOORD2; };
            V vert(A a)
            {
                Piece piece = PieceOf(a.instance);
                V o;
                o.p = TransformWorldToHClip(mul(piece.objectToWorld, float4(a.p, 1)).xyz);
                o.bary = a.bary.xyz;
                o.edges = a.edges;
                o.light = piece.colour.rgb;
                return o;
            }
            half4 frag(V i):SV_Target
            {
                CHAMPIONS_ID_PASS(_ChampionsId)
                float3 d=i.bary/max(fwidth(i.bary),1e-5);
                float3 e=(1-smoothstep(.35,1.85,d))*i.edges;
                float edge=max(e.x,max(e.y,e.z));
                return half4(lerp(half3(.0024,.0027,.004),i.light,edge),1);
            }
            ENDHLSL
        }
    }
}
