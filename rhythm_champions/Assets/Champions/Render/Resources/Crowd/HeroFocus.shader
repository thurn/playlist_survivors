// A soft, local reduction of competing light. Threats (3050) and the hero (3060) draw after it.
Shader "Champions/HeroFocus"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+40" }
        Pass
        {
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Champions/Render/Shaders/IdPass.hlsl"
            struct V { float4 p:SV_POSITION; float2 local:TEXCOORD0; };
            V vert(float3 p:POSITION) { V o; o.p=TransformObjectToHClip(p); o.local=p.xy; return o; }
            half4 frag(V i):SV_Target
            {
                if (_ChampionsIdPass > .5) discard;
                float cover = .96 * (1 - smoothstep(.35, 1, length(i.local)));
                // Preserve the actual object IDs beneath this contrast-only layer.
                return half4(0, 0, 0, cover);
            }
            ENDHLSL
        }
    }
}
