// Shared S^3 projection for the transparent shaders (S3/Beam, S3/Glow). S3/Object has its own copy.
#ifndef S3_CORE_INCLUDED
#define S3_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float4x4 _S3Model;   // per renderer: object frame in SO(4)
float4x4 _S3View;    // global: R^4 -> eye frame (x right, y up, -z forward, w)
float4 _S3Eye;       // global: eye position on S^3
float _S3R, _S3InvR, _S3Near;

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

float3 S3SRGBToLinear(float3 col)
{
    #ifndef UNITY_COLORSPACE_GAMMA
        col = max(col, 0);
        col = col <= 0.04045 ? col / 12.92 : pow((col + 0.055) / 1.055, 2.4);
    #endif
    return col;
}

#endif
