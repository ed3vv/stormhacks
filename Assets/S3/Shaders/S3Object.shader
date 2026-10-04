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
        _Dither ("See-through (holograms)", Float) = 0
        _Glow ("Glow", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Ground;
            float _Dither;
            float _Glow;
        CBUFFER_END

        float4x4 _S3Model;   // per renderer: object frame in SO(4)
        float4x4 _S3View;    // global: R^4 -> eye frame (x right, y up, -z forward, w)
        float4 _S3Eye;       // global: eye position on S^3
        float4 _S3Hole;      // cup position on S^3
        float _S3R, _S3InvR, _S3Near, _S3CupR, _S3GreenR;

        // per-map look (see S3World.Style)
        float4 _S3Sun;            // fixed R^4 vector; its tangent part is the light direction
        float4 _S3SunColor, _S3SkyAmbient, _S3GroundAmbient;
        float4 _S3Haze;           // rgb haze colour, a = strength towards the antipode
        float _S3Biome;           // 0 meadow, 1 desert, 2 eternal poles
        float4 _S3Patch[16];      // desert grass patches / basin lakes: centre on S^3
        float _S3PatchR[16];      // ... and radius in metres
        float _S3PatchCount;

        // see-through props: one eased value per prop, computed on the CPU by S3World.UpdateFades
        float _S3PropFade[512];
        float _S3Fade;            // master switch

        static const float4 Wind = float4(0.62, 0, 0.36, 0.7);

        struct Attributes
        {
            float3 pos      : POSITION;
            float3 normal   : NORMAL;
            float4 color    : COLOR;
            float4 prop     : TEXCOORD1;   // prop foot on S^3 (wind phase)
            float4 propInfo : TEXCOORD2;   // (fade index, 1-based or 0, sway, 0, 0)
        };

        struct Varyings
        {
            float4 clip  : SV_POSITION;
            float4 color : COLOR;
            float4 p4    : TEXCOORD0;   // point of S^3 (world)
            float4 n4    : TEXCOORD1;   // tangent normal (world)
            float4 q     : TEXCOORD2;   // point in the eye frame
            float fade   : TEXCOORD3;   // 0 solid .. 1 see-through
        };

        float S3Dist(float4 a, float4 b) { return 2 * asin(saturate(length(a - b) * 0.5)); }

        // ordered 4x4 dither threshold in (0, 1)
        static const float BayerMatrix[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        float Bayer4(float2 pixel)
        {
            uint2 p = uint2(pixel) & 3;
            return (BayerMatrix[p.y * 4 + p.x] + 0.5) / 16.0;
        }

        void SeeThrough(Varyings i)
        {
            if (i.fade > 0.01 && Bayer4(i.clip.xy) < i.fade * 0.78) discard;   // keep ~1/5 of the pixels
        }

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
            float4 p = mul(_S3Model, pl);
            o.n4 = mul(_S3Model, nl);

            // wind: push grass and foliage sideways, more the higher above the ground
            float sway = a.propInfo.y;
            if (sway > 0)
            {
                float h = max(asin(clamp(p.y, -1, 1)) * _S3R, 0);
                float phase = dot(a.prop, float4(12.9, 7.1, 5.3, 3.7)) * 6.2831;
                float wave = sin(_Time.y * 1.7 + phase) * 0.7 + sin(_Time.y * 2.9 + phase * 1.3) * 0.3;
                float amount = sway * wave * min(0.12 * h + 0.05 * h * h, 0.35);   // metres
                float4 up = normalize(float4(0, 1, 0, 0) - p.y * p);
                float4 w = Wind - dot(Wind, p) * p;
                w = normalize(w - dot(w, up) * up + 1e-6);
                p = normalize(p + w * (amount * _S3InvR));
            }
            o.p4 = p;
            float4 q = mul(_S3View, p);
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
            int idx = (int)(a.propInfo.x + 0.5);
            o.fade = max(idx > 0 ? _S3PropFade[min(idx - 1, 511)] * _S3Fade : 0, _Dither);
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

        float3 MeadowGround(float3 g, float n1, float n2, float n3)
        {
            float3 grass = lerp(float3(0.36, 0.62, 0.24), float3(0.50, 0.74, 0.30), n1);
            return grass * (0.86 + 0.18 * n2 + 0.08 * n3);
        }

        float3 DesertGround(float4 p, float3 g, float n1, float n2, float n3)
        {
            // wind-rippled sand
            float rip = sin(g.x * 2.3 + g.y * 1.1 + g.z * 0.7 + n2 * 5) * 0.5 + 0.5;
            float3 sand = lerp(float3(0.88, 0.69, 0.51), float3(0.96, 0.81, 0.63), n1);
            sand *= (0.94 + 0.08 * rip) * (0.95 + 0.07 * n3);
            // oases of lush grass around the registered patches, with a noisy edge
            float patch = 0;
            for (int k = 0; k < 16; k++)
            {
                if (k >= (int)_S3PatchCount) break;
                float dm = S3Dist(p, _S3Patch[k]) * _S3R;
                float edge = _S3PatchR[k] * (0.75 + 0.5 * n2);
                patch = max(patch, 1 - smoothstep(edge - 1.0, edge, dm));
            }
            float3 grass = lerp(float3(0.50, 0.62, 0.20), float3(0.70, 0.75, 0.28), n2) * (0.85 + 0.2 * n3);
            return lerp(sand, grass, patch);
        }

        float3 BasinGround(float4 p, float3 g, float n1, float n2, float n3)
        {
            // snow with soft blue hollows, wind ripples (sastrugi) and a sparkle here and there
            float3 snow = lerp(float3(0.93, 0.96, 0.99), float3(0.79, 0.85, 0.93), smoothstep(0.55, 0.85, n1) * 0.7);
            snow *= 0.95 + 0.06 * n2;
            float sastrugi = sin(g.x * 1.7 - g.y * 2.3 + n2 * 4) * 0.5 + 0.5;
            snow *= 0.97 + 0.04 * sastrugi;
            if (Hash(floor(g * 5)) > 0.993) snow = float3(1.05, 1.05, 1.1);
            // frozen lakes (the registered patches) with cracks and glints; purely cosmetic
            float lake = 0;
            for (int k = 0; k < 16; k++)
            {
                if (k >= (int)_S3PatchCount) break;
                float dl = S3Dist(p, _S3Patch[k]) * _S3R - _S3PatchR[k] - (n1 - 0.5) * 1.4;
                lake = max(lake, 1 - smoothstep(-0.35, 0, dl));
            }
            if (lake <= 0) return snow;
            float3 ice = lerp(float3(0.62, 0.89, 0.96), float3(0.43, 0.76, 0.90), smoothstep(0.2, 0.8, n2));
            float nc = Noise(g * 0.9 + float3(7, 0, 0)), nc2 = Noise(g * 2.1 + float3(0, 3, 0));
            float crack = max(1 - smoothstep(0.012, 0.03, abs(nc - 0.5)), 1 - smoothstep(0.006, 0.018, abs(nc2 - 0.5)));
            ice = lerp(ice, float3(0.29, 0.56, 0.78), crack * 0.75);
            if (Hash(floor(g * 4)) > 0.992) ice = float3(1, 1, 1);
            return lerp(snow, ice, lake);
        }

        float3 GroundColor(float4 p)
        {
            // the ground is the unit 2-sphere in (x, z, w); scale to metres for the noise
            float3 g = float3(p.x, p.z, p.w) * _S3R;
            float n1 = Noise(g * 0.22), n2 = Noise(g * 1.1), n3 = Noise(g * 5.0);
            float3 col = _S3Biome > 1.5 ? BasinGround(p, g, n1, n2, n3)
                       : _S3Biome > 0.5 ? DesertGround(p, g, n1, n2, n3) : MeadowGround(g, n1, n2, n3);

            float dh = S3Dist(p, _S3Hole) * _S3R;   // metres from the cup
            float green = 1 - smoothstep(_S3GreenR - 0.25, _S3GreenR, dh);
            float3 putting = (_S3Biome > 1.5 ? float3(0.84, 0.90, 0.97) : float3(0.47, 0.78, 0.34)) * (0.96 + 0.06 * n3);
            float stripe = step(0.5, frac(g.x * 0.5));
            putting *= 0.97 + 0.05 * stripe;
            col = lerp(col, putting, green);
            float cup = 1 - smoothstep(_S3CupR - 0.015, _S3CupR + 0.015, dh);
            float rim = (1 - smoothstep(_S3CupR + 0.015, _S3CupR + 0.06, dh)) * (1 - cup);
            col = lerp(col, float3(0.94, 0.94, 0.90), rim * 0.65);
            return lerp(col, _S3Biome > 1.5 ? float3(0.10, 0.14, 0.20) : float3(0.04, 0.05, 0.03), cup);
        }

        float4 S3Frag(Varyings i) : SV_Target
        {
            SeeThrough(i);
            float4 p = normalize(i.p4);
            float4 n = normalize(i.n4 - dot(i.n4, p) * p);
            float4 toEye = _S3Eye - dot(_S3Eye, p) * p;
            float eyeLen = length(toEye);
            toEye = eyeLen > 1e-6 ? toEye / eyeLen : n;
            if (dot(n, toEye) < 0) n = -n;          // two-sided: always light the face we see

            float4 L = _S3Sun - dot(_S3Sun, p) * p;
            float lenL = length(L);
            L = lenL > 1e-5 ? L / lenL : n;
            float diff = saturate(dot(n, L));
            float4 up = float4(0, 1, 0, 0) - p.y * p;
            float upLen = length(up);
            float hemi = upLen > 1e-5 ? dot(n, up / upLen) * 0.5 + 0.5 : 1;

            float3 base = i.color.rgb * _Color.rgb;
            if (_Ground > 0.5) base *= GroundColor(p);
            float3 ambient = lerp(_S3GroundAmbient.rgb, _S3SkyAmbient.rgb, hemi);
            float3 col = base * (ambient + _S3SunColor.rgb * diff);
            // soft rim of sunlight on silhouettes
            float rim = pow(1 - saturate(dot(n, toEye)), 4);
            col += base * _S3SunColor.rgb * rim * 0.35 * (0.4 + 0.6 * diff);
            // emissive: vertex alpha below 1 (lenses, crystals) plus a per-renderer glow
            col += base * ((1 - i.color.a) * 1.6 + _Glow);

            float t = atan2(length(i.q.xyz), i.q.w);   // geodesic distance from the eye
            col = lerp(col, _S3Haze.rgb, _S3Haze.a * smoothstep(0.4 * PI, PI, t));
            // colours above are authored in sRGB; the project renders in linear space
            #ifndef UNITY_COLORSPACE_GAMMA
                col = max(col, 0);
                col = col <= 0.04045 ? col / 12.92 : pow((col + 0.055) / 1.055, 2.4);
            #endif
            return float4(col, 1);
        }

        float4 DepthFrag(Varyings i) : SV_Target { SeeThrough(i); return 0; }
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
