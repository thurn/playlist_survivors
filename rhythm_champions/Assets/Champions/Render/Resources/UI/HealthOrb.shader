Shader "Champions/HealthOrb"
{
    Properties { _Fill ("Health", Float) = 1 _Motion ("Active seconds", Float) = 0 }
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Fill, _Motion;
            float4 frag(v2f_img i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float r = length(p);
                float aa = .015;
                float disc = 1 - smoothstep(.91, .93, r);
                float rim = smoothstep(.91, .93, r) * (1 - smoothstep(.97, .99, r));
                // Motion tapers at the endpoints so zero is truly empty and full truly full.
                float wave = sin(p.x * 9 + _Motion * 2.4) * .022 + sin(p.x * 17 - _Motion * 1.7) * .012;
                float surface = lerp(-.93, .93, saturate(_Fill)) + wave * saturate(_Fill * 20) * saturate((1-_Fill)*20);
                float filled = (1 - smoothstep(surface-aa, surface+aa, p.y)) * step(.0001, _Fill);
                float energy = .5 + .5 * sin(p.y * 22 + sin(p.x * 7 + _Motion) * 2 - _Motion * 3);
                float boundary = (1 - smoothstep(.01, .04, abs(p.y-surface))) * step(.0001,_Fill);
                float3 liquid = lerp(float3(.45,.012,.035), float3(.95,.06,.11), energy * .4 + .3);
                float3 c = lerp(float3(.022,.009,.022), liquid, filled) * disc;
                c += boundary * disc * float3(.5,.22,.24);
                c += rim * float3(.65,.14,.2);
                return float4(c, max(disc,rim));
            }
            ENDHLSL
        }
    }
}
