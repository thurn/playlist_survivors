// The crystal shatter's shards (crowd.md, Monster styles; CrystalShatter): Crystal.shader's surface
// (CrystalCommon.hlsl) on a baked chunk of a monster's death pose, drawn by a MeshRenderer. The skin
// is the style mesh's own triangles, so the assembled shards match the crowd's crystal exactly;
// inside faces (UV1.x 1) are brighter and more saturated, and are drawn only once the shards are
// released (_ShardOpen, per renderer), so nothing pokes out of the assembled body.
//
// Cracks (CrackGlow): over the death hold, glowing seams spread along the cuts between shards (the
// skin's edges marked in UV2.xyz: 1 on a cut, 0.3 on a hairline crack inside a chunk) from where the
// blow landed (_Crack, per renderer), thin at the front and wider behind it; _CrackHeat builds them
// through the hold, flares on the release and cools as the shards fly, while the fresh inside faces
// glow with the same heat.
//
// The material draws in Crystal's queue, which gets no motion vectors from URP, so moving shards
// would smear under TAA. CrystalShatter gives every shard a second material on this shader in the
// last opaque queue (2499) with only its MotionVectors pass enabled: URP draws that pass for opaque
// queues, so each shard writes its own object motion (the renderer's previous matrix), and TAA
// reprojects it. The twin writes no colour and no depth, so the opaque texture behind the shard is
// untouched.
Shader "Demo3D/Styles/CrystalShard"
{
    Properties
    {
        [HDR] _Tint ("Gem", Color) = (0.55, 0.25, 1.0, 1)
        _Refraction ("Refraction", Float) = 0.06
        _Facets ("Faceting", Range(0, 1)) = 0.9
        _Gain ("Horde gain (MonsterStyle tuning)", Float) = 1
        _Peak ("Horde peak (MonsterStyle tuning)", Float) = 1000
        _HeightM ("Height at scale 1 (m)", Float) = 1.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint; float _Refraction; half _Facets;
            half _Gain; half _Peak;
            float _HeightM;
        CBUFFER_END

        // Per renderer (a MaterialPropertyBlock): 1 once the shards are released, which shows the
        // inside faces; the cracks' origin (xyz, world: where the blow landed) and how far they have
        // spread from it (w, m); and their heat (0 draws none).
        float _ShardOpen;
        float4 _Crack;
        float _CrackHeat;

        #include "StyleCommon.hlsl"

        struct ShardAttributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 inside : TEXCOORD1; // x 1 on an inside face
            float4 seam : TEXCOORD2;   // xyz: the edge facing that barycentric's corner: 1 a cut, 0.3 a hairline, 0 none
            float4 bary : TEXCOORD3;   // the style mesh's barycentrics and value per triangle
        };

        // An inside face of an unreleased shard draws nothing (all three corners at the origin).
        bool ShardHidden(ShardAttributes v) { return v.inside.x > 0.5 && _ShardOpen < 0.5; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include_with_pragmas "StylePragmas.hlsl"
            #include "CrystalCommon.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 bary : TEXCOORD2;
                float inside : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                float3 seam : TEXCOORD5;
            };

            Varyings vert(ShardAttributes v)
            {
                Varyings o = (Varyings)0;
                if (ShardHidden(v)) return o;
                o.positionWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.bary = v.bary;
                o.inside = v.inside.x;
                o.seam = v.seam.xyz;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // The cracks' light at a fragment (HDR, added after the grade so the bloom catches it).
            half3 CrackGlow(Varyings i)
            {
                // Derivatives first, outside any per-triangle branch.
                float px = max(length(ddx(i.positionWS)), length(ddy(i.positionWS))); // metres per pixel
                float3 edgePx = i.bary.xyz / max(fwidth(i.bary.xyz), 1e-5);          // pixels to each edge
                half3 hot = lerp(_Tint.rgb, half3(1, 1, 1), 0.4);
                if (i.inside > 0.5) return hot * (_CrackHeat * 0.3 * _ShardOpen);
                // The front: a sphere from the blow, ragged per triangle (its value, bary.w).
                float d = distance(i.positionWS, _Crack.xyz) + i.bary.w * 0.15;
                float behind = saturate((_Crack.w - d) / 0.3); // 0 at the front, 1 0.3 m behind it
                float widthPx = lerp(0.002, 0.008, behind) / max(px, 1e-5);
                float3 core = i.seam * (1.0 - smoothstep(widthPx - 0.75, widthPx + 0.75, edgePx));
                float3 halo = i.seam * exp(-edgePx * px / 0.025);
                float line_ = max(core.x, max(core.y, core.z));
                float glow = max(halo.x, max(halo.y, halo.z));
                return hot * (step(0.001, behind) * _CrackHeat * (line_ * (2.5 + 2.0 * (1.0 - behind)) + glow * 0.35));
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Demo3DIdMask > 0.5) return half4(1, 1, 1, 1); // TD15's ID render, as the crowd's
                half4 c = CrystalShade(i.positionWS, i.normalWS, i.positionCS, i.bary, 0, 0, i.inside, i.fogFactor);
                if (_CrackHeat > 0) c.rgb += CrackGlow(i);
                return c;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(ShardAttributes v) : SV_POSITION
            {
                if (ShardHidden(v)) return 0;
                float3 positionWS = TransformObjectToWorld(v.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }
            ColorMask RG
            Cull Back

            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
}
