// Clears the static shadow map's depth to the far plane over the viewport (StaticShadowMap.Redraw):
// a full-screen triangle at the far clip value, so only the region being redrawn is cleared.
Shader "Hidden/Game/ShadowClear"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZTest Always
            ZWrite On
            Cull Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2 - 1, UNITY_RAW_FAR_CLIP_VALUE, 1);
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
