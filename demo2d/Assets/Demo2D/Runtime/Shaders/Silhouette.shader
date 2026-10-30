// PX9's hero outline: the sprite's shape in one flat colour (_Color, HDR), unlit and alpha
// blended. SpriteCrowd draws it four times under the hero, a texel off each way, so the knight
// carries a one-texel rim that reads in any crowd.
Shader "Demo2D/Silhouette"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite", 2D) = "white" {}
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // One pass for the 2D renderer, as Glow.shader. The sprite inputs and flip are URP's 2D ones,
        // so the outline follows the hero's facing.
        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.color = v.color * unity_SpriteColor;
                o.uv = v.uv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * i.color.a * _Color.a;
                return float4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
