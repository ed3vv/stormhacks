#include <metal_stdlib>
using namespace metal;

struct Uniforms {
    float2 resolution;
    float2 padding;
    float4 player;
    float4 forward;
    float4 right;
    float4 up;
    float4 golfBall;
    float4 hole;
    float4 aim;
    float4 game;
};

struct RasterData { float4 position [[position]]; };

struct BoxSpec {
    float3 anchor;
    float3 axisX;
    float3 axisY;
    float3 halfSize;
    float3 color;
    float4 sphereCenter;
    float sphereCosRadius;
};

constant BoxSpec boxes[14] = {
    { float3(-0.1582667,-0.5514225,-0.8190756), float3(0.9611929,-0.2758772,0), float3(-0.2259643,-0.7872897,0.5736855), float3(0.05729,0.0441781,0.0591369), float3(0.749337,0.0678391,0.6326528), float4(-0.1579907,-0.5504608,-0.8176472,0.0590338), 0.9883227 },
    { float3(-0.4729068,-0.5030331,-0.7234064), float3(0.7285874,-0.6849528,0), float3(-0.4954992,-0.5270648,0.6904225), float3(0.0279613,0.0362004,0.1319101), float3(0.2567048,0.2758504,0.8992962), float4(-0.4688454,-0.498713,-0.7171936,0.1307773), 0.9744772 },
    { float3(0.9806302,-0.1956148,-0.0099635), float3(0.1956245,0.9806789,0), float3(0.009771,-0.0019491,0.9999504), float3(0.0258073,0.0334518,0.0521049), float3(0.0643311,0.6275307,0.7329694), float4(0.9793017,-0.1953498,-0.00995,0.0520343), 0.9939301 },
    { float3(-0.9222494,-0.2086856,-0.3254327), float3(0.2206993,-0.9753419,0), float3(-0.3174081,-0.0718228,0.9455652), float3(0.0912092,0.0392725,0.0722301), float3(0.7376245,0.0652113,0.6465656), float4(-0.919853,-0.2081433,-0.3245871,0.0720424), 0.9800853 },
    { float3(-0.338032,-0.5352655,-0.7740964), float3(0.8455106,-0.5339586,0), float3(-0.4133354,-0.6545067,0.6330678), float3(0.0511273,0.0691915,0.0632492), float3(0.3689378,0.1785569,0.8884523), float4(-0.3373579,-0.5341981,-0.7725526,0.0631231), 0.9848314 },
    { float3(-0.3316531,0.9352964,0.1233968), float3(-0.9424996,-0.3342073,0), float3(0.0412401,-0.1163014,0.9923574), float3(0.0332443,0.0283977,0.0885131), float3(0.8200965,0.5321173,0.1001972), float4(-0.3303615,0.931654,0.1229162,0.0881684), 0.9869913 },
    { float3(0.7786598,-0.625421,-0.0503737), float3(0.626216,0.7796496,0), float3(0.0392738,-0.0315448,0.9987304), float3(0.0835847,0.0368342,0.0447193), float3(0.5992175,0.7754769,0.0696562), float4(0.7778824,-0.6247966,-0.0503234,0.0446747), 0.9862112 },
    { float3(-0.0077993,-0.9517934,-0.3066407), float3(0.9999664,-0.008194,0), float3(-0.0025126,-0.3066304,0.9518253), float3(0.03638,0.0305436,0.0934684), float3(0.7719863,0.6038921,0.074777), float4(-0.0077654,-0.9476628,-0.30531,0.0930627), 0.9853638 },
    { float3(0.5966121,0.5668338,0.568114), float3(-0.6887829,0.7249677,0), float3(-0.4118643,-0.3913072,0.8229499), float3(0.0573414,0.069398,0.2121751), float3(0.3918472,0.1626172,0.8823186), float4(0.5836199,0.5544901,0.5557424,0.2075546), 0.9346914 },
    { float3(0.8240269,-0.5635792,-0.0579502), float3(0.5645279,0.825414,0), float3(0.0478329,-0.0327145,0.9983195), float3(0.0447087,0.0623424,0.1461799), float3(0.301719,0.8991329,0.2326422), float4(0.8153614,-0.5576526,-0.0573408,0.1446427), 0.9648452 },
    { float3(0.2587637,-0.9428267,0.2100455), float3(0.9643396,0.2646681,0), float3(-0.0555923,0.2025552,0.9776916), float3(0.044286,0.0401495,0.0874475), float3(0.3175326,0.2188723,0.8976663), float4(0.25778,-0.9392423,0.2092469,0.0871151), 0.9850676 },
    { float3(0.7525857,0.577199,-0.316948), float3(-0.6085753,0.7934961,0), float3(0.251497,0.1928868,0.9484429), float3(0.0386598,0.0344847,0.1369906), float3(0.5959637,0.777876,0.0703921), float4(0.7456219,0.571858,-0.3140153,0.135723), 0.9720206 },
    // Wide, low platforms intended for chip shots.
    { float3(0.8869949,0.4617792,0), float3(-0.4617792,0.8869949,0), float3(0,0,1), float3(0.18,0.14,0.018), float3(0.83,0.48,0.18), float4(0.8868513,0.4617044,0,0.0179971), 0.9355802 },
    // Final cuboid is the white flag pole at the cup.
    { float3(-0.0791209,0.9968650,0), float3(-0.9968650,-0.0791209,0), float3(0,0,1), float3(0.006,0.006,0.14), float3(0.94,0.94,0.94), float4(-0.0783567,0.9872371,0,0.1386478), 0.9742513 }
};

float sphereIntersection(float4 player, float4 direction, float4 center,
                         float radius, float limit) {
    float a = dot(player, center);
    float b = dot(direction, center);
    float range = sqrt(a * a + b * b);
    float cosineRadius = cos(radius);
    if (cosineRadius > range) { return limit; }
    float phase = atan2(b, a);
    float delta = acos(clamp(cosineRadius / range, -1.0, 1.0));
    float first = fmod(phase - delta + 6.2831853, 6.2831853);
    float second = fmod(phase + delta + 6.2831853, 6.2831853);
    float result = limit;
    if (first > 0.001 && first < result) { result = first; }
    if (second > 0.001 && second < result) { result = second; }
    return result;
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
                              constant Uniforms &u [[buffer(0)]]) {
    float2 uv = (in.position.xy * 2.0 - u.resolution) / u.resolution.y;
    uv.y = -uv.y;
    uv *= 0.64;
    float curvedVertical = uv.y - 0.105 * uv.x * uv.x;
    float4 direction = normalize(u.forward + uv.x * u.right
                                           + curvedVertical * u.up);

    float groundDistance = fmod(atan2(-u.player.w, direction.w) + 3.14159265,
                                3.14159265);
    float best = groundDistance;
    int hitBox = -1;
    int hitSphere = 0;
    float3 hitLocal = 0.0;
    float3 hitHalfSize = 1.0;
    float3 hitColor = 1.0;

    if (u.game.y < 0.5) {
        float ballRadius = u.game.z;
        float ballHit = sphereIntersection(u.player, direction, u.golfBall,
                                           ballRadius, best);
        if (ballHit < best) {
            best = ballHit;
            hitSphere = 1;
        }
    }

    float4 flagCenter = normalize(float4(u.hole.xyz, 0.31));
    float flagHit = sphereIntersection(u.player, direction, flagCenter,
                                       0.055, best);
    if (flagHit < best) {
        best = flagHit;
        hitSphere = 2;
    }

    // Analytical geodesic ray/cuboid intersections. A bounding sphere rejects
    // nearly every box before the six exact face tests are needed.
    for (int i = 0; i < 14; ++i) {
        constant BoxSpec &box = boxes[i];
        float3 anchor = box.anchor;
        float3 axisX = box.axisX;
        float3 axisY = box.axisY;
        float3 halfSize = box.halfSize;
        float3 objectColor = box.color;
        float height = halfSize.z * 2.0;

        float sphereA = dot(u.player, box.sphereCenter);
        float sphereB = dot(direction, box.sphereCenter);
        float sphereRange = sqrt(sphereA * sphereA + sphereB * sphereB);
        if (box.sphereCosRadius > sphereRange) { continue; }

        float spherePhase = atan2(sphereB, sphereA);
        float sphereDelta = acos(clamp(box.sphereCosRadius / sphereRange,
                                       -1.0, 1.0));
        float sphereNear = fmod(spherePhase - sphereDelta + 6.2831853,
                                6.2831853);
        float sphereFar = fmod(spherePhase + sphereDelta + 6.2831853,
                               6.2831853);
        bool cameraInsideBounds = sphereA > box.sphereCosRadius;
        if (!cameraInsideBounds && sphereNear >= best && sphereFar >= best) {
            continue;
        }

        float denominatorStart = dot(u.player.xyz, anchor);
        float denominatorDirection = dot(direction.xyz, anchor);
        float3 numeratorStart = float3(dot(u.player.xyz, axisX),
                                       dot(u.player.xyz, axisY), u.player.w);
        float3 numeratorDirection = float3(dot(direction.xyz, axisX),
                                           dot(direction.xyz, axisY), direction.w);
        float3 boxCenter = float3(0.0, 0.0, height * 0.5);

        for (int faceIndex = 0; faceIndex < 6; ++faceIndex) {
            int dimension = faceIndex / 2;
            float side = (faceIndex & 1) == 0 ? -1.0 : 1.0;
            float boundary = boxCenter[dimension]
                           + side * halfSize[dimension];
            float coefficientA = numeratorStart[dimension]
                               - boundary * denominatorStart;
            float coefficientB = numeratorDirection[dimension]
                               - boundary * denominatorDirection;
            float candidate = fmod(atan2(-coefficientA, coefficientB)
                                   + 3.14159265, 3.14159265);
            if (candidate <= 0.00005 || candidate >= best) { continue; }

            float cosine = cos(candidate);
            float sine = sin(candidate);
            float denominator = denominatorStart * cosine
                              + denominatorDirection * sine;
            if (denominator <= 0.02) { continue; }
            float3 local = (numeratorStart * cosine
                          + numeratorDirection * sine) / denominator;
            float3 centered = local - boxCenter;
            float3 excess = abs(centered) - halfSize;
            if (excess.x <= 0.0005 && excess.y <= 0.0005
                && excess.z <= 0.0005) {
                best = candidate;
                hitBox = i;
                hitLocal = centered;
                hitHalfSize = halfSize;
                hitColor = objectColor;
                hitSphere = 0;
            }
        }
    }

    if (hitBox >= 0) {
        float3 face = abs(hitLocal) / max(hitHalfSize, float3(0.0001));
        float faceLight;
        if (face.z > face.x && face.z > face.y) {
            faceLight = 1.12;
        } else if (face.x > face.y) {
            faceLight = 0.72;
        } else {
            faceLight = 0.88;
        }
        return float4(hitColor * faceLight, 1.0);
    }

    if (hitSphere > 0) {
        float4 surfacePoint = u.player * cos(best) + direction * sin(best);
        if (hitSphere == 1) {
            float4 normal = normalize(surfacePoint * dot(surfacePoint, u.golfBall)
                                    - u.golfBall);
            float highlight = 0.62 + 0.38 * abs(dot(normal, u.player));
            return float4(float3(0.96, 0.97, 1.0) * highlight, 1.0);
        }
        return float4(0.92, 0.08, 0.06, 1.0);
    }

    float4 groundPoint = u.player * cos(groundDistance)
                       + direction * sin(groundDistance);
    float4 groundNormal = normalize(float4(0.0, 0.0, 0.0, 1.0)
                                  - groundPoint * groundPoint.w);
    float4 light = normalize(u.player
                           - groundPoint * dot(groundPoint, u.player));
    float diffuse = abs(dot(groundNormal, light));
    float longitude = atan2(groundPoint.y, groundPoint.x);
    float latitude = asin(clamp(groundPoint.z, -1.0, 1.0));
    float checker = fmod(floor(longitude * 1.9099)
                       + floor(latitude * 3.8197), 2.0);
    float3 base = mix(float3(0.13, 0.36, 0.20),
                      float3(0.55, 0.62, 0.35), checker);
    const float3 sky = float3(0.30, 0.50, 0.82);
    float3 groundColor = mix(sky, base * (0.35 + 0.75 * diffuse),
                             exp(-0.13 * groundDistance));

    if (dot(groundPoint.xyz, u.hole.xyz) > cos(u.hole.w)) {
        groundColor = float3(0.018, 0.022, 0.025);
    } else if (u.game.x < 0.5 && u.game.y < 0.5) {
        float3 ballGround = normalize(u.golfBall.xyz);
        float3 side = normalize(cross(ballGround, u.aim.xyz));
        float along = atan2(dot(groundPoint.xyz, u.aim.xyz),
                            dot(groundPoint.xyz, ballGround));
        float lateral = asin(clamp(dot(groundPoint.xyz, side), -1.0, 1.0));
        const float guideStart = 0.045;
        const float guideLength = 0.72;
        const float dotSpacing = 0.052;
        if (along > guideStart && along < guideLength) {
            float dotNumber = floor((along - guideStart) / dotSpacing + 0.5);
            float dotCenter = guideStart + dotNumber * dotSpacing;
            float dotDistance = length(float2(along - dotCenter, lateral));
            float circle = 1.0 - smoothstep(0.007, 0.011, dotDistance);
            float fade = pow(clamp(1.0 - along / guideLength, 0.0, 1.0),
                             0.72);
            groundColor = mix(groundColor, float3(1.0), circle * fade);
        }
    }
    return float4(groundColor, 1.0);
}
