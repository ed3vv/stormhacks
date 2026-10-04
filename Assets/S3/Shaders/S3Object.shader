// Renders meshes living in the 3-sphere S^3 (HyperEngine-style, but positively curved).
//
// Each vertex is authored in local metres. The vertex shader
//   1. maps it onto S^3 with the exponential map at e_w (and parallel-transports its normal),
//   2. places it with the per-renderer SO(4) frame _S3Model,
//   3. moves it into the eye's frame with _S3View (an SO(4) rotation, z flipped to Unity's view space),
//   4. projects the 4D view point (x, y, z, w) homogeneously: the screen position depends only on
//      the direction (x, y, z), which is exactly where the great circle from the eye reaches it.
//      Great circles map to straight lines, so triangles stay triangles.
// Depth is taken from w = cos(distance): it is monotonic along the whole half-lap (0, pi), so
// objects past the equator (distance > pi/2) still sort correctly - and grow as they recede.
// That's the "reverse perspective" of spherical space. The other image of every
// object (distance > pi) always lies behind the ground, so a single pass is enough.
Shader "S3/Object"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _Ground ("Ground pattern", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Ground;
        CBUFFER_END

        float4x4 _S3Model;   // per renderer: object frame in SO(4)
        float4x4 _S3View;    // global: R^4 -> eye frame (x right, y up, -z forward, w)
        float4 _S3Eye;       // global: eye position on S^3
        float4 _S3Sun;       // global: fixed R^4 vector; its tangent part is the light direction
        float4 _S3Haze;      // rgb haze colour, a = strength towards the antipode
        float4 _S3Hole;      // cup position on S^3
        float _S3R, _S3InvR, _S3Near, _S3Ambient, _S3CupR, _S3GreenR;

        struct Attributes
        {
            float3 pos    : POSITION;
            float3 normal : NORMAL;
            float4 color  : COLOR;
        };

        struct Varyings
        {
            float4 clip  : SV_POSITION;
            float4 color : COLOR;
            float4 p4    : TEXCOORD0;   // point of S^3 (world)
            float4 n4    : TEXCOORD1;   // tangent normal (world)
            float4 q     : TEXCOORD2;   // point in the eye frame
        };

        Varyings S3Vert(Attributes a)
        {
            float3 u = a.pos * _S3InvR;
            float r = length(u);
            float3 d = r > 1e-6 ? u / r : float3(0, 0, 0);
            float s = sin(r), c = cos(r);
            float4 pl = float4(d * s, c);
            float along = dot(a.normal, d);
            float4 nl = float4(a.normal - along * d, 0) + along * float4(d * c, -s);

            Varyings o;
            o.p4 = mul(_S3Model, pl);
            o.n4 = mul(_S3Model, nl);
            float4 q = mul(_S3View, o.p4);
            o.q = q;

            // xy and w from the camera's own projection applied to the direction (x, y, z)
            float4 clip = mul(UNITY_MATRIX_P, float4(q.xyz, 0));
            // depth from w: ndc = 1/2 + n/2 * cot(distance) straight ahead, linear in (z, w)
            #if UNITY_REVERSED_Z
                clip.z = 0.5 * clip.w + 0.5 * _S3Near * q.w;
            #else
                clip.z = UNITY_NEAR_CLIP_VALUE < 0 ? -_S3Near * q.w : 0.5 * clip.w - 0.5 * _S3Near * q.w;
            #endif
            o.clip = clip;
            o.color = a.color;
            return o;
        }

        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                             lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                             lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float S3Dist(float4 a, float4 b) { return 2 * asin(saturate(length(a - b) * 0.5)); }

        float3 GroundColor(float4 p)
        {
            // the ground is the unit 2-sphere in (x, z, w); scale to metres for the noise
            float3 g = float3(p.x, p.z, p.w) * _S3R;
            float n1 = Noise(g * 0.22), n2 = Noise(g * 1.1), n3 = Noise(g * 5.0);
            float3 grass = lerp(float3(0.36, 0.62, 0.24), float3(0.50, 0.74, 0.30), n1);
            grass *= 0.86 + 0.18 * n2 + 0.08 * n3;

            float dh = S3Dist(p, _S3Hole) * _S3R;   // metres from the cup
            float green = 1 - smoothstep(_S3GreenR - 0.25, _S3GreenR, dh);
            float3 putting = float3(0.47, 0.78, 0.34) * (0.96 + 0.06 * n3);
            float stripe = step(0.5, frac(g.x * 0.5));
            putting *= 0.97 + 0.05 * stripe;
            grass = lerp(grass, putting, green);
            float cup = 1 - smoothstep(_S3CupR - 0.012, _S3CupR + 0.012, dh);
            float rim = (1 - smoothstep(_S3CupR + 0.012, _S3CupR + 0.04, dh)) * (1 - cup);
            grass = lerp(grass, float3(0.92, 0.92, 0.88), rim * 0.6);
            return lerp(grass, float3(0.04, 0.05, 0.03), cup);
        }

        float4 S3Frag(Varyings i) : SV_Target
        {
            float4 p = normalize(i.p4);
            float4 n = normalize(i.n4 - dot(i.n4, p) * p);
            float4 toEye = _S3Eye - dot(_S3Eye, p) * p;
            if (dot(n, toEye) < 0) n = -n;          // two-sided: always light the face we see

            float4 L = _S3Sun - dot(_S3Sun, p) * p;
            float lenL = length(L);
            L = lenL > 1e-5 ? L / lenL : n;
            float diff = saturate(dot(n, L));

            float3 base = i.color.rgb * _Color.rgb;
            if (_Ground > 0.5) base *= GroundColor(p);
            float3 col = base * (_S3Ambient + (1 - _S3Ambient) * diff);

            float t = atan2(length(i.q.xyz), i.q.w);   // geodesic distance from the eye
            col = lerp(col, _S3Haze.rgb, _S3Haze.a * smoothstep(0.45 * PI, PI, t));
            // colours above are authored in sRGB; the project renders in linear space
            #ifndef UNITY_COLORSPACE_GAMMA
                col = col <= 0.04045 ? col / 12.92 : pow((col + 0.055) / 1.055, 2.4);
            #endif
            return float4(col, 1);
        }

        float4 DepthFrag(Varyings i) : SV_Target { return 0; }
        ENDHLSL

        Pass
        {
            Name "S3Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex S3Vert
            #pragma fragment S3Frag
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex S3Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }
    }
}
