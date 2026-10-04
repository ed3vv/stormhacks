// Additive glow for meshes in S^3 (the barrier dome over a sealed cup).
// Vertex colour rgb is the glow (authored in sRGB), alpha its strength. _Shimmer makes it ripple.
Shader "S3/Glow"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Shimmer ("Shimmer", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "S3Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "S3Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Shimmer;
            CBUFFER_END

            struct Attributes
            {
                float3 pos   : POSITION;
                float4 color : COLOR;
                float4 local : TEXCOORD3;   // curtains (w = 1): (angle along the loop, height up the curtain 0..1)
            };

            struct Varyings
            {
                float4 clip  : SV_POSITION;
                float4 color : COLOR;
                float4 p4    : TEXCOORD0;
                float4 local : TEXCOORD1;
            };

            Varyings Vert(Attributes a)
            {
                Varyings o;
                float4 p = mul(_S3Model, S3Exp(a.pos));
                float4 q;
                o.clip = S3Clip(p, q);
                o.color = a.color;
                o.p4 = p;
                o.local = a.local;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 p = i.p4 * _S3R;
                float wave = sin(dot(p, float4(0.9, 0.35, 0.7, 0.55)) * 1.3 - _Time.y * 0.8)
                           * sin(dot(p, float4(-0.4, 0.2, 0.8, -0.6)) * 2.1 + _Time.y * 1.3);
                float shimmer = 1 + _Shimmer * 0.5 * wave;
                float curtain = 1;
                if (i.local.w > 0.5)
                {
                    // aurora curtains: fine vertical rays drifting along, a bright lower hem fading upwards
                    float along = i.local.x, up = i.local.y;
                    float rays = S3Noise(float3(along * 38, _Time.y * 0.12, 0.5)) * 0.65 + S3Noise(float3(along * 110 + 3, _Time.y * 0.3, 2.5)) * 0.35;
                    rays = 0.35 + 1.2 * smoothstep(0.25, 0.8, rays);
                    float hem = smoothstep(0, 0.04, up) * lerp(1.6, 0.25, smoothstep(0.03, 1, up));
                    curtain = rays * hem * smoothstep(1, 0.85, up);
                }
                float3 col = S3SRGBToLinear(i.color.rgb * _Color.rgb) * i.color.a * _Intensity * shimmer * curtain;
                float3 scatter;
                col *= S3Fog(normalize(i.p4), scatter);   // fades into the haze like everything else
                return float4(max(col, 0), 0);
            }
            ENDHLSL
        }
    }
}
