// Ambient particles (pollen, dust, snowflakes) in a box of air around the eye.
//
// Each quad is one particle: its corner (position.xy = -1/+1), a seed position in the box (uv1.xyz,
// 0..1) and size in metres (uv1.w), and a velocity in m/s with a flutter rate (uv2). The vertex
// shader moves it, wraps it around the box (so the box never runs out of particles), maps it onto
// S^3 with the box's frame (_S3Model, at the eye's foot) and makes it a camera-facing sprite. The
// box's frame keeps a steady heading and _S3PShift carries how far the eye has walked, so the
// particles stay put in the world while the box follows the eye.
Shader "S3/Particles"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "S3Particles"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "S3Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
            CBUFFER_END

            float4 _S3PShift;   // metres the eye has moved (box axes x, z)
            float4 _S3PBox;     // box size (m) xyz, w: 1 to light the particles with the sun (dust, snow)

            struct Attributes
            {
                float3 pos   : POSITION;    // (corner x, corner y, 0)
                float4 color : COLOR;       // rgb, a = opacity (0 = purely additive glow)
                float4 seed  : TEXCOORD1;   // (seed in the box 0..1, size m)
                float4 vel   : TEXCOORD2;   // (velocity m/s, flutter rate)
            };

            struct Varyings
            {
                float4 clip   : SV_POSITION;
                float4 color  : COLOR;
                float2 corner : TEXCOORD0;
                float fade    : TEXCOORD1;
                float4 p4     : TEXCOORD2;
            };

            Varyings Vert(Attributes a)
            {
                Varyings o;
                float t = _Time.y;
                float3 box = _S3PBox.xyz;
                float3 lp = a.seed.xyz * box + a.vel.xyz * t - float3(_S3PShift.x, 0, _S3PShift.z);
                float w = a.vel.w;
                lp += float3(sin(t * w + a.seed.x * 40), 0.5 * sin(t * w * 1.3 + a.seed.y * 37), cos(t * w * 0.8 + a.seed.z * 23)) * 0.2;
                float3 cell = lp / box;
                cell = frac(cell + float3(0.5, 0, 0.5)) - float3(0.5, 0, 0.5);   // xz centred on the eye, y from the ground up
                lp = cell * box;
                // fade in and out near the walls of the box so wrapping never pops
                float edge = max(abs(cell.x), abs(cell.z)) * 2;
                o.fade = (1 - smoothstep(0.7, 1, edge)) * smoothstep(0, 0.08, cell.y) * (1 - smoothstep(0.85, 1, cell.y));

                float4 p = mul(_S3Model, S3Exp(lp));
                float4 q;
                float4 clip = S3Clip(p, q);
                // a sprite size metres across: angular size size / (R sin(distance)), pushed out in clip space
                float s = max(length(q.xyz), 1e-4);
                float dist = atan2(s, q.w) * _S3R;
                float angle = a.seed.w * _S3InvR / s;
                clip.xy += a.pos.xy * angle * float2(UNITY_MATRIX_P[0][0], UNITY_MATRIX_P[1][1]) * clip.w;
                o.fade *= smoothstep(0.35, 1.2, dist);   // not right in your eye
                o.clip = clip;
                o.color = a.color;
                o.corner = a.pos.xy;
                o.p4 = p;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.corner);
                float soft = saturate(1 - r);
                soft *= soft;
                float a = soft * i.fade;
                float3 col = S3SRGBToLinear(i.color.rgb) * _Intensity;
                if (_S3PBox.w > 0.5) col *= S3SRGBToLinear(_S3SunColor.rgb * 0.9 + _S3SkyAmbient.rgb * 0.5);
                float3 scatter;
                col *= S3Fog(normalize(i.p4), scatter);
                // premultiplied: opacity from the vertex alpha, the rest adds light (a glow)
                return float4(col * a, a * i.color.a);
            }
            ENDHLSL
        }
    }
}
