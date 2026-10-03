#include <metal_stdlib>
using namespace metal;

struct Uniforms {
    float2 resolution;
    float2 padding;
    float4 player;
    float4 forward;
    float4 right;
    float4 up;
    float4 ball;
};

struct RasterData { float4 position [[position]]; };

float grassHash(float3 p) {
    p = fract(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return fract((p.x + p.y) * p.z);
}

float grassNoise(float3 p) {
    float3 cell = floor(p);
    float3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float n000 = grassHash(cell);
    float n100 = grassHash(cell + float3(1, 0, 0));
    float n010 = grassHash(cell + float3(0, 1, 0));
    float n110 = grassHash(cell + float3(1, 1, 0));
    float n001 = grassHash(cell + float3(0, 0, 1));
    float n101 = grassHash(cell + float3(1, 0, 1));
    float n011 = grassHash(cell + float3(0, 1, 1));
    float n111 = grassHash(cell + float3(1, 1, 1));
    return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
               mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

vertex RasterData fullscreenVertex(uint vertexID [[vertex_id]]) {
    const float2 vertices[3] = {
        float2(-1.0, -1.0), float2(3.0, -1.0), float2(-1.0, 3.0)
    };
    RasterData out;
    out.position = float4(vertices[vertexID], 0.0, 1.0);
    return out;
}

fragment float4 worldFragment(RasterData in [[stage_in]],
                              constant Uniforms &u [[buffer(0)]],
                              texture2d<float> lettering [[texture(0)]],
                              texture2d<float> grassTexture [[texture(1)]]) {
    float2 uv = (in.position.xy * 2.0 - u.resolution) / u.resolution.y;
    uv.y = -uv.y;
    float4 direction = normalize(u.forward + uv.x * u.right + uv.y * u.up);

    float best = 6.2831853;
    bool hitBall = false;
    float radius = sqrt(max(1.0 - u.ball.w * u.ball.w, 0.0));
    float a = dot(u.player, u.ball);
    float b = dot(direction, u.ball);
    float radial = sqrt(a * a + b * b);
    if (radius <= radial) {
        float phi = atan2(b, a);
        float delta = acos(clamp(radius / radial, -1.0, 1.0));
        for (int k = 0; k < 2; ++k) {
            float t = fmod(phi + (k == 0 ? -delta : delta) + 6.2831853, 6.2831853);
            if (t > 0.001 && t < best) { best = t; hitBall = true; }
        }
    }

    float groundDistance = fmod(atan2(-u.player.w, direction.w) + 3.14159265,
                                3.14159265);
    bool hitGround = false;
    if (groundDistance > 0.001 && groundDistance < best) {
        best = groundDistance;
        hitGround = true;
        hitBall = false;
    }

    const float3 sky = float3(0.3, 0.5, 0.82);
    float3 color = sky;
    float4 point = u.player * cos(best) + direction * sin(best);
    float4 light = normalize(u.player - point * dot(point, u.player));
    if (hitGround) {
        float4 normal = normalize(float4(0.0, 0.0, 0.0, 1.0) - point * point.w);
        float diffuse = abs(dot(normal, light));
        float3 surfacePoint = point.xyz;
        float broadVariation = grassNoise(surfacePoint * 7.0);
        constexpr sampler grassSampler(coord::normalized,
                                       address::repeat,
                                       filter::linear);
        float textureScale = 4.5;
        float3 blendWeights = pow(abs(surfacePoint), 5.0);
        blendWeights /= max(blendWeights.x + blendWeights.y + blendWeights.z,
                            0.0001);
        float3 grassXY = grassTexture.sample(grassSampler,
                                             surfacePoint.xy * textureScale).rgb;
        float3 grassYZ = grassTexture.sample(grassSampler,
                                             surfacePoint.yz * textureScale).rgb;
        float3 grassZX = grassTexture.sample(grassSampler,
                                             surfacePoint.zx * textureScale).rgb;
        float3 base = grassYZ * blendWeights.x
                    + grassZX * blendWeights.y
                    + grassXY * blendWeights.z;
        base *= 0.82 + broadVariation * 0.34;
        float3 litGrass = base * (0.30 + 0.90 * diffuse);
        color = mix(sky, litGrass, exp(-0.13 * best));
    } else if (hitBall) {
        float4 normal = normalize(point * dot(point, u.ball) - u.ball);
        float diffuse = abs(dot(normal, light));
        float3 base = 0.55 + 0.45 * cos(6.2831 * float3(0.0, 0.33, 0.67));
        float checker = sign(sin(14.0 * point.x) * sin(14.0 * point.y) * sin(14.0 * point.z));
        base *= checker > 0.0 ? 1.0 : 0.6;
        float3 lit = base * (0.25 + 0.9 * diffuse) + 0.2 * pow(diffuse, 24.0);
        color = mix(sky, lit, exp(-0.13 * best));

        // Map the lettering around the geodesic ball's upper surface.
        float4 localDirection = normalize((point - u.ball * radius) / u.ball.w);
        float3 centerDirection = normalize(u.ball.xyz);
        float4 topAxis = float4(-centerDirection * u.ball.w, radius);
        float4 horizontalAxis = float4(0.0, 0.0, 1.0, 0.0);
        float4 verticalAxis = normalize(float4(-centerDirection.y,
                                                centerDirection.x, 0.0, 0.0));
        float2 labelPosition = float2(dot(localDirection, horizontalAxis),
                                     dot(localDirection, verticalAxis));
        float2 labelUV = float2(0.5 + labelPosition.x / 1.45,
                                0.5 - labelPosition.y / 0.52);
        bool onTop = dot(localDirection, topAxis) > 0.68;
        bool insideLabel = all(labelUV >= 0.0) && all(labelUV <= 1.0);
        constexpr sampler textSampler(coord::normalized,
                                      address::clamp_to_zero,
                                      filter::linear);
        float4 ink = lettering.sample(textSampler, labelUV);
        float labelAmount = (onTop && insideLabel) ? ink.a : 0.0;
        color = mix(color, float3(1.0, 0.93, 0.35), labelAmount);
    }
    return float4(color, 1.0);
}
