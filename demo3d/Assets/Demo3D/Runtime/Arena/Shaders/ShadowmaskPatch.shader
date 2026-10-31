// The ground's shadowmask without the broken structures (ArenaRuntime.Break; environment.md, Breaks on
// screen): a blit over one lightmap's shadowmask that keeps every texel but the ground's whose ray to the
// moon passes through a broken piece's box (StaticShadow.hlsl's Demo3DUnderBroken), and there writes the
// static shadow map's visibility, re-rendered without the pieces, into the moon's channel. The ground is
// the only lightmapped surface, and its lightmap UVs are planar over its rectangle, so a texel's world
// position follows from its UV.
Shader "Hidden/Demo3D/ShadowmaskPatch"
{
    Properties
    {
        _MainTex ("Shadowmask", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StaticShadow.hlsl"

            TEXTURE2D(_MainTex);
            float4 _GroundST;     // the ground renderer's lightmapScaleOffset
            float4 _GroundRect;   // its rectangle on the ground: x, z of its minimum corner, then its size
            float4 _MoonChannel;  // the moon's shadowmask channel, one-hot
            float3 _ToMoon;       // the direction to the moon

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 mask = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, i.uv);
                float2 g = (i.uv - _GroundST.zw) / _GroundST.xy;
                if (any(g < 0) || any(g > 1)) return mask;
                float3 p = float3(_GroundRect.x + g.x * _GroundRect.z, 0.02, _GroundRect.y + g.y * _GroundRect.w);
                if (!Demo3DUnderBroken(p, _ToMoon)) return mask;
                return lerp(mask, Demo3DStaticShadow(p).xxxx, _MoonChannel);
            }
            ENDHLSL
        }
    }
}
