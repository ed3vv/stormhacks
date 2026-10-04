// Shared code for the S^3 shaders: the projection, procedural noise and the atmosphere.
#ifndef S3_CORE_INCLUDED
#define S3_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float4x4 _S3Model;   // per renderer: object frame in SO(4)
float4x4 _S3View;    // global: R^4 -> eye frame (x right, y up, -z forward, w)
float4 _S3Eye;       // global: eye position on S^3
float _S3R, _S3InvR, _S3Near;

// per-map look (see S3World.Style); colours are authored in sRGB
float4 _S3Sun;            // a point of S^3: sunlight arrives along the geodesics from it
float4 _S3SunColor, _S3SkyAmbient, _S3GroundAmbient;
float4 _S3Haze;           // rgb: the air near the ground
float4 _S3SkyColor;       // rgb: the air looking up, and what glossy surfaces reflect
float4 _S3Fog;            // x: extinction per metre at the ground, y: scale height (m), z: glow towards the sun, w: sky tint

// Exponential map at e_w: local metres -> point of S^3.
float4 S3Exp(float3 pos)
{
    float3 u = pos * _S3InvR;
    float r = length(u);
    float3 d = r > 1e-6 ? u / r : float3(0, 0, 0);
    return float4(d * sin(r), cos(r));
}

// A point of S^3 to clip space, with the same depth as S3/Object.
float4 S3Clip(float4 p, out float4 q)
{
    q = mul(_S3View, p);
    float4 clip = mul(UNITY_MATRIX_P, float4(q.xyz, 0));
    // depth from w: ndc = 1/2 + n/2 * cot(distance) straight ahead, linear in (z, w)
    #if UNITY_REVERSED_Z
        clip.z = 0.5 * clip.w + 0.5 * _S3Near * q.w;
    #else
        clip.z = UNITY_NEAR_CLIP_VALUE < 0 ? -_S3Near * q.w : 0.5 * clip.w - 0.5 * _S3Near * q.w;
    #endif
    return clip;
}

// The vector orthogonal to a, b and c (generalised cross product in R^4).
float4 S3Cross(float4 a, float4 b, float4 c)
{
    return float4( dot(a.yzw, cross(b.yzw, c.yzw)),
                  -dot(a.xzw, cross(b.xzw, c.xzw)),
                   dot(a.xyw, cross(b.xyw, c.xyw)),
                  -dot(a.xyz, cross(b.xyz, c.xyz)));
}

float S3Dist(float4 a, float4 b) { return 2 * asin(saturate(length(a - b) * 0.5)); }

float3 S3SRGBToLinear(float3 col)
{
    #ifndef UNITY_COLORSPACE_GAMMA
        col = max(col, 0);
        col = col <= 0.04045 ? col / 12.92 : pow((col + 0.055) / 1.055, 2.4);
    #endif
    return col;
}

// ------------------------------------------------------------------ noise

float S3Hash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float3 S3Hash3(float3 p)
{
    return float3(S3Hash(p), S3Hash(p + float3(17.31, 5.17, 3.71)), S3Hash(p + float3(41.97, 23.13, 11.03)));
}

// Perlin's twelve gradient directions
static const float3 S3Grads[12] =
{
    float3(1, 1, 0), float3(-1, 1, 0), float3(1, -1, 0), float3(-1, -1, 0),
    float3(1, 0, 1), float3(-1, 0, 1), float3(1, 0, -1), float3(-1, 0, -1),
    float3(0, 1, 1), float3(0, -1, 1), float3(0, 1, -1), float3(0, -1, -1),
};

float S3GradDot(float3 i, float3 f) { return dot(S3Grads[(int)(S3Hash(i) * 11.999)], f); }

// Gradient noise in [0, 1] around 0.5, quintic fade: smooth blobs with no square lattice showing.
float S3Noise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    float3 u = f * f * f * (f * (f * 6 - 15) + 10);
    float v = lerp(lerp(lerp(S3GradDot(i, f), S3GradDot(i + float3(1, 0, 0), f - float3(1, 0, 0)), u.x),
                        lerp(S3GradDot(i + float3(0, 1, 0), f - float3(0, 1, 0)), S3GradDot(i + float3(1, 1, 0), f - float3(1, 1, 0)), u.x), u.y),
                   lerp(lerp(S3GradDot(i + float3(0, 0, 1), f - float3(0, 0, 1)), S3GradDot(i + float3(1, 0, 1), f - float3(1, 0, 1)), u.x),
                        lerp(S3GradDot(i + float3(0, 1, 1), f - float3(0, 1, 1)), S3GradDot(i + float3(1, 1, 1), f - float3(1, 1, 1)), u.x), u.y), u.z);
    return saturate(0.5 + 0.85 * v);
}

// How much of a pattern with this wavelength (metres) to show at px metres per pixel: none below
// about 2 pixels (it would only shimmer), all of it from 5 pixels up.
float S3Detail(float wavelength, float px) { return saturate((wavelength / max(px, 1e-6) - 2) / 3); }

// fbm in [-0.5, 0.5]-ish around zero; octaves too fine for the pixel fade out instead of aliasing
float S3Fbm(float3 x, float px, int octaves)
{
    float sum = 0, amp = 0.5, freq = 1;
    [loop] for (int k = 0; k < octaves; k++)
    {
        float fade = S3Detail(1 / freq, px);
        if (fade <= 0) break;
        sum += (S3Noise(x * freq + k * 7.13) - 0.5) * amp * fade;
        freq *= 2.03; amp *= 0.5;
    }
    return sum;
}

// Cheaper cellular noise: only the 8 cells around the nearest corner (features kept near their cell's
// middle so it is rarely wrong). Distances to the nearest and second nearest feature, and a random
// number for the nearest cell.
float3 S3VoronoiFast(float3 x)
{
    float3 i = floor(x), f = frac(x);
    float3 o = step(0.5, f) - 1;   // the 2x2x2 block of cells nearest this point
    float d1 = 8, d2 = 8, id = 0;
    [unroll] for (int z = 0; z <= 1; z++)
    [unroll] for (int y = 0; y <= 1; y++)
    [unroll] for (int k = 0; k <= 1; k++)
    {
        float3 c = o + float3(k, y, z);
        float3 h = S3Hash3(i + c);
        float3 r = c + 0.25 + 0.5 * h - f;
        float d = dot(r, r);
        if (d < d1) { d2 = d1; d1 = d; id = h.z; } else if (d < d2) d2 = d;
    }
    return float3(sqrt(float2(d1, d2)), id);
}

// Cellular noise in 3D: distance to the nearest and second nearest feature (cell size 1).
float2 S3Voronoi(float3 x)
{
    float3 i = floor(x), f = frac(x);
    float d1 = 8, d2 = 8;
    [unroll] for (int z = -1; z <= 1; z++)
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int k = -1; k <= 1; k++)
    {
        float3 c = float3(k, y, z);
        float3 r = c + S3Hash3(i + c) - f;
        float d = dot(r, r);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return sqrt(float2(d1, d2));
}

// ------------------------------------------------------------------ atmosphere

// Unit tangent at the eye pointing along the geodesic to p, and the distance to p (radians).
float4 S3ViewRay(float4 p, out float dist)
{
    float c = dot(_S3Eye, p);
    float4 d = p - c * _S3Eye;
    float dl = length(d);
    dist = atan2(dl, c);
    return dl > 1e-6 ? d / dl : float4(0, 1, 0, 0);
}

// Air that thins with height (density exp(-h / scale height)), integrated along the geodesic from
// the eye. Rays skimming the ground pass through a lot of it, so the horizon is hazy, while rays
// looking up spend most of their way high above the ground: the antipode overhead stays clear.
// Returns the transmittance; haze is the air's colour without the glow towards the sun (sRGB).
float S3FogAir(float4 p, out float3 haze)
{
    float dist;
    float4 d = S3ViewRay(p, dist);
    float4 e = _S3Eye;
    float ds = dist / 10, tau = 0, scale = max(_S3Fog.y, 0.1);
    [unroll] for (int k = 0; k < 10; k++)
    {
        float s = (k + 0.5) * ds;
        float y = e.y * cos(s) + d.y * sin(s);
        float h = asin(clamp(y, -1, 1)) * _S3R;
        tau += exp(-max(h, 0) / scale);
    }
    // haze near the ground, sky looking up
    float4 up = float4(0, 1, 0, 0) - e.y * e;
    float upLen = length(up);
    float elev = upLen > 1e-5 ? dot(d, up / upLen) : 1;
    haze = lerp(_S3Haze.rgb, _S3SkyColor.rgb, saturate(elev * 1.6 + 0.05) * _S3Fog.w);
    return exp(-tau * _S3Fog.x * ds * _S3R);
}

// The air glows looking towards the sun (cheap enough per pixel, where it stays sharp).
float3 S3SunGlow(float4 p)
{
    float dist;
    float4 d = S3ViewRay(p, dist);
    float4 toSun = _S3Sun - dot(_S3Sun, _S3Eye) * _S3Eye;
    float sunLen = length(toSun);
    float towards = sunLen > 1e-5 ? saturate(dot(d, toSun / sunLen)) : 0;
    return _S3SunColor.rgb * _S3Fog.z * (pow(towards, 6) * 0.6 + pow(towards, 40) * 0.8);
}

// Transmittance; scatter is the colour the air adds (sRGB).
float S3Fog(float4 p, out float3 scatter)
{
    float trans = S3FogAir(p, scatter);
    scatter += S3SunGlow(p);
    return trans;
}

#endif
