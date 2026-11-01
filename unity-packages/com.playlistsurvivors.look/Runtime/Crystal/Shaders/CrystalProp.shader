// The void world's crystal props and formations in a structure look (neon-look.md, The Axes > World; the style
// guide's Crystal props): self-lit, never by the scene's lights, in the palette's shatter.gem. CrystalKit's meshes
// carry their faces' edges (uv2.x 0 on a face's edges, 1 at its centre) and a shard's inside faces (uv1.x 1), so
// every look draws the facets' real edges as lines a fixed number of pixels wide. StructureLook's options set:
// - _Body, _Shade: the faces' own light (a share of the gem) and how much it falls off toward grazing views;
// - _Rim, _RimPower: a fresnel rim, lighter (or, negative, darker) toward the silhouette;
// - _Edge, _EdgePx, _EdgeDark: the edge lines' light and width, or dark lines (1) for a bright body;
// - _Refraction with _REFRACT: the scene behind (the void and the grid) seen through the gem, bent by its facets;
// - _Pulse: how much the beat's pulse (_SL_Pulse) flares the edges.
// Per renderer, as the structure shatter sets them (the game's structure shaders' names): _SL_PieceFx (x the
// hit's flash, z 1 on a shard, w its break glow), _SL_Tint (a gem colour; alpha 0 keeps _Tint), _SL_Sink and
// _HeightM (a regrowing piece under the ground, in its heights).
Shader "PS/Look/CrystalProp"
{
    Properties
    {
        [HDR] _Tint ("Gem (linear)", Color) = (1, 0.87, 0.6, 1)
        _Body ("Body light", Float) = 0.04
        _Shade ("Facet falloff", Range(0, 1)) = 0.3
        _Rim ("Rim", Float) = 0.5
        _RimPower ("Rim power", Float) = 3
        _Edge ("Edge light", Float) = 1.2
        _EdgePx ("Edge width (px)", Float) = 1.4
        _EdgeDark ("Dark edges", Range(0, 1)) = 0
        _Refraction ("Refraction", Float) = 0
        _Pulse ("Beat flare", Float) = 0.35
        _HeightM ("Piece height (m), per renderer", Float) = 2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        // After the opaques, so a refracting gem sees the void, the grid and the horde behind it.
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _Body; half _Shade; half _Rim; half _RimPower;
            half _Edge; half _EdgePx; half _EdgeDark;
            float _Refraction; half _Pulse;
            float _HeightM;
        CBUFFER_END
        float4 _SL_PieceFx;
        float4 _SL_Tint;
        float _SL_Sink;
        float _SL_Pulse;

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv1 : TEXCOORD1;
            float2 uv2 : TEXCOORD2;
        };

        float3 PropWorld(float3 positionOS)
        {
            float3 p = TransformObjectToWorld(positionOS);
            p.y -= _SL_Sink * _HeightM;
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _REFRACT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float edge : TEXCOORD2;
                nointerpolation float inside : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = PropWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.edge = v.uv2.x;
                o.inside = _SL_PieceFx.z > 0.5 ? v.uv1.x : 0;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = saturate(abs(dot(n, view)));
                float3 tint = _SL_Tint.a > 0 ? _SL_Tint.rgb : _Tint.rgb;

                // The facets' edges: a line _EdgePx pixels wide, whatever the distance.
                float w = max(fwidth(i.edge), 1e-5) * _EdgePx;
                float line_ = 1 - smoothstep(0.5 * w, 1.5 * w, i.edge);

                float3 body = tint * (_Body * lerp(1, 0.35 + 0.65 * ndv, _Shade) + _Rim * pow(1 - ndv, _RimPower));
                #if _REFRACT
                float2 screen = i.positionCS.xy / _ScaledScreenParams.xy;
                float3 nv = mul((float3x3)UNITY_MATRIX_V, n);
                body += SampleSceneColor(saturate(screen + nv.xy * _Refraction)) * tint * 0.85;
                #endif
                body = max(body, 0);
                float3 lit = tint * _Edge * (1 + _Pulse * _SL_Pulse);
                float3 edge = lerp(lit, body * 0.15, _EdgeDark);
                float3 color = lerp(body, edge, line_);

                // A break: the whole piece blooms, a shard's fresh inside faces glow hotter; a hit flashes toward white.
                float glow = saturate(_SL_PieceFx.w);
                color *= 1 + 1.5 * glow;
                color += i.inside * glow * tint * 2.5;
                color += _SL_PieceFx.x * (tint * 1.5 + 0.8);
                return half4(MixFog(color, i.fogFactor), 1);
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
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 p = PropWorld(v.positionOS);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - p);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, lightDirectionWS)));
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
