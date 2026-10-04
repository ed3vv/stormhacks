// Laser light: ribbons along great circles of S^3 that always turn their face to the eye.
//
// Each vertex carries its segment's start point x and direction d (uv1, uv2), its distance t along
// the segment (position.y, radians) and which edge of the ribbon it is on (position.x = -1 or +1).
// The vertex shader walks to x cos t + d sin t, lifts it to the drawing height, and pushes it
// sideways along the one direction orthogonal to the point, the beam and the eye (a 4D cross
// product), so the ribbon is seen face-on from anywhere. Far beams keep about a pixel of width.
Shader "S3/Beam"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "S3Beam"
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

            struct Attributes
            {
                float3 pos   : POSITION;    // (edge -1/+1, t, 0)
                float4 color : COLOR;
                float4 start : TEXCOORD1;
                float4 dir   : TEXCOORD2;
                float4 info  : TEXCOORD3;   // (half width m, lift m, alpha, 0)
            };

            struct Varyings
            {
                float4 clip  : SV_POSITION;
                float4 color : COLOR;
                float2 edge  : TEXCOORD0;   // (edge, metres along the beam)
                float alpha  : TEXCOORD1;
            };

            Varyings Vert(Attributes a)
            {
                float t = a.pos.y;
                float st, ct;
                sincos(t, st, ct);
                float4 P = a.start * ct + a.dir * st;
                float4 T = a.dir * ct - a.start * st;

                float4 up = float4(0, 1, 0, 0) - P.y * P;
                float ul = length(up);
                up = ul > 1e-5 ? up / ul : T;
                float lift = a.info.y * _S3InvR;
                P = normalize(P * cos(lift) + up * sin(lift));
                T = normalize(T - dot(T, P) * P);

                float4 E = _S3Eye - dot(_S3Eye, P) * P;
                float el = length(E);
                E = el > 1e-6 ? E / el : up;
                float4 N = S3Cross(P, T, E);
                float nl = length(N);
                if (nl < 1e-4) { N = S3Cross(P, T, up); nl = length(N); }
                N = nl > 1e-6 ? N / nl : float4(0, 0, 0, 0);

                float dist = acos(clamp(dot(P, _S3Eye), -1, 1));
                float halfWidth = max(a.info.x, 0.0016 * sin(dist) * _S3R);
                float4 p = normalize(P + N * (a.pos.x * halfWidth * _S3InvR));

                Varyings o;
                float4 q;
                o.clip = S3Clip(p, q);
                o.color = a.color;
                o.edge = float2(a.pos.x, t * _S3R);
                // thin far-away ribbons are widened, so fade them to keep the same brightness
                o.alpha = a.info.z * saturate(a.info.x / halfWidth * 1.5);
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float prof = saturate(1 - abs(i.edge.x));
                float core = prof * prof * prof;
                float flow = 0.85 + 0.15 * sin(i.edge.y * 4 - _Time.y * 14);
                float a = saturate(core + 0.45 * prof * prof) * i.alpha;
                // saturated colour with a thin hot core (white only at the very centre)
                float3 col = i.color.rgb * _Intensity * (1 + 0.8 * core) * flow + 0.35 * core * core * _Intensity;
                return float4(col * a, a * 0.85);
            }
            ENDHLSL
        }
    }
}
