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
//
// Surfaces: every vertex carries a material (S3Mat in MeshBuilder.cs) and its position in the prop's
// own metres, so each material can draw a procedural texture that stays fixed to the prop. Things
// near the antipode are seen hugely magnified, so all the detail is resolution-free and fades in
// only once it is big enough on screen (crisp when magnified, no shimmer in the distance).
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
        #include "S3Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Ground;
            float _Dither;
            float _Glow;
        CBUFFER_END

        float4 _S3Hole;           // cup position on S^3
        float4 _S3Tee;            // tee position on S^3
        float4 _S3Ball;           // the golf ball's centre on S^3
        float4 _S3BallInfo;       // x: radius (m), y: 1 when there is a ball
        float _S3CupR, _S3GreenR;
        float _S3Biome;           // 0 meadow, 1 desert, 2 aurora basin
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
            float4 local    : TEXCOORD3;   // (position in the prop's own metres, material)
        };

        struct Varyings
        {
            float4 clip  : SV_POSITION;
            float4 color : COLOR;
            float4 p4    : TEXCOORD0;   // point of S^3 (world)
            float4 n4    : TEXCOORD1;   // tangent normal (world)
            float4 q     : TEXCOORD2;   // point in the eye frame
            float fade   : TEXCOORD3;   // 0 solid .. 1 see-through
            float3 lp    : TEXCOORD4;   // position in the prop's own metres
            nointerpolation float mat : TEXCOORD5;
            float4 air   : TEXCOORD6;   // the haze between the eye and here: rgb colour, a transmittance
        };

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
            float4 q;
            o.clip = S3Clip(p, q);
            o.q = q;
            o.color = a.color;
            int idx = (int)(a.propInfo.x + 0.5);
            o.fade = max(idx > 0 ? _S3PropFade[min(idx - 1, 511)] * _S3Fade : 0, _Dither);
            o.lp = a.local.xyz;
            o.mat = a.local.w;
            float3 haze;
            o.air.a = S3FogAir(p, haze);   // smooth enough to interpolate; the sun's glow is added per pixel
            o.air.rgb = haze;
            return o;
        }

        // ================================================================== surfaces

        struct Surface
        {
            float3 albedo;
            float4 n;          // shading normal (tangent at p)
            float spec;        // specular strength
            float gloss;       // specular exponent
            float trans;       // light shining through from behind (leaves, blades, cloth)
            float fresnel;     // how much of the sky it reflects at grazing angles
            float sparkle;     // glints that light up in the sun (snow, ice)
            float3 emit;
        };

        // Planar coordinates on a flat face: the two local axes it spans most.
        float2 FaceUV(float3 lp, float3 nl)
        {
            float3 a = abs(nl);
            if (a.y >= a.x && a.y >= a.z) return lp.xz;
            return a.x >= a.z ? lp.zy : lp.xy;
        }

        // Thin bright glints in cells of the given size: a few cells light up, changing as you move.
        float Glints(float3 x, float cell, float px, float rarity)
        {
            float3 ci = floor(x / cell);
            float h = S3Hash(ci);
            float tw = frac(h * 91.7 + dot(_S3Eye, float4(7.3, 5.1, 6.7, 3.9)) * 3 + _Time.y * 0.15);
            return step(rarity, h) * smoothstep(0.75, 1, tw) * S3Detail(cell * 1.5, px);
        }

        // Rectangular blocks (bricks, ice blocks) on a face: 1 inside a block, 0 in the joints.
        float Blocks(float2 uv, float rowH, float len, float joint, float px, out float id)
        {
            float r = uv.y / rowH, rid = floor(r);
            float c = uv.x / len + rid * 0.5, cid = floor(c);
            id = S3Hash(float3(rid, cid, 7.7));
            float2 f = float2(frac(c) * len, frac(r) * rowH);
            float edge = min(min(f.x, len - f.x), min(f.y, rowH - f.y));
            return smoothstep(joint * 0.5 - px, joint * 0.5 + px, edge);
        }

        void Material(inout Surface s, float mat, float3 lp, float3 nl, float px, float upness)
        {
            int m = (int)(mat + 0.5);
            [branch] if (m == 0) return;

            [branch] if (m == 1)   // grass blades: dark base, light tips, faint veins
            {
                s.albedo *= lerp(0.62, 1.2, saturate(lp.y / 0.5));
                s.albedo *= 1 + (S3Noise(float3(lp.x * 95, lp.y * 6, lp.z * 95)) - 0.5) * 0.3 * S3Detail(0.011, px);
                s.trans = 0.65; s.spec = 0.06; s.gloss = 16;
            }
            else if (m == 2)   // leaves: clumps, then overlapping single leaves up close
            {
                s.albedo *= 0.78 + 0.36 * S3Noise(lp * 4.5);
                float fade = S3Detail(0.09, px);
                if (fade > 0)
                {
                    float3 v = S3VoronoiFast(lp * 12);
                    float leaf = smoothstep(0.0, 0.3, v.y - v.x);          // a little darker where leaves overlap
                    float tint = 0.8 + 0.4 * v.z;                         // every leaf its own shade
                    float3 shade = lerp(float3(1, 1, 1), float3(1.08, 1.06, 0.82), saturate(v.z * 2 - 1.4));   // a few yellowing
                    s.albedo *= lerp(1, (0.8 + 0.24 * leaf) * tint * shade, fade);
                }
                s.trans = 0.45; s.spec = 0.12; s.gloss = 28;
            }
            else if (m == 3)   // bark: vertical fibres and fine grain
            {
                float ridges = smoothstep(0.3, 0.7, S3Noise(float3(lp.x * 26, lp.y * 2.2, lp.z * 26)));
                s.albedo *= lerp(0.92, 0.7 + 0.45 * ridges, S3Detail(0.04, px));
                s.albedo *= 1 + (S3Noise(float3(lp.x * 85, lp.y * 7, lp.z * 85)) - 0.5) * 0.3 * S3Detail(0.012, px);
            }
            else if (m == 4)   // stone: mottling, grain, speckles, veins; moss on top in the meadow
            {
                s.albedo *= 0.82 + 0.32 * S3Noise(lp * 3.1);
                s.albedo *= 1 + (S3Noise(lp * 38) - 0.5) * 0.24 * S3Detail(0.026, px);
                s.albedo *= 1 + (S3Hash(floor(lp * 90)) - 0.5) * 0.2 * S3Detail(0.011, px);
                float vein = pow(saturate(1 - abs(S3Noise(lp * 6.5 + 2) * 2 - 1)), 14);
                s.albedo *= 1 - vein * 0.35 * S3Detail(0.03, px);
                if (_S3Biome < 0.5)
                {
                    float moss = smoothstep(0.5, 0.72, S3Noise(lp * 4.3 + 11)) * saturate(upness * 1.6);
                    s.albedo = lerp(s.albedo, float3(0.40, 0.54, 0.22) * (0.85 + 0.3 * S3Noise(lp * 40)), moss * 0.7);
                }
                s.spec = 0.08; s.gloss = 20;
            }
            else if (m == 5)   // plaster: soft mottling, grain, a little damp along the bottom
            {
                s.albedo *= 0.95 + 0.1 * S3Noise(lp * 2.3);
                s.albedo *= 1 + (S3Noise(lp * 31) - 0.5) * 0.12 * S3Detail(0.032, px);
                s.albedo *= 1 + (S3Noise(float3(lp.x * 9, lp.y * 0.7, lp.z * 9)) - 0.5) * 0.05;
                s.albedo *= lerp(0.88, 1, saturate(lp.y / 0.8));
            }
            else if (m == 6)   // roof tiles: rows of curved tiles running down the slope
            {
                float2 slope = nl.xz;
                float sl = length(slope);
                float2 down = sl > 0.05 ? slope / sl : float2(1, 0);
                float u = dot(lp.xz, down), v = dot(lp.xz, float2(-down.y, down.x));
                float row = u / 0.24, rid = floor(row), fr = frac(row);
                float colm = v / 0.2 + rid * 0.5, cid = floor(colm), fc = frac(colm);
                float shade = (0.78 + 0.22 * sin(fc * PI)) * (1 - 0.4 * smoothstep(0.72, 1, fr));
                float tint = 0.88 + 0.22 * S3Hash(float3(rid, cid, 3));
                s.albedo *= lerp(1, shade * tint, S3Detail(0.2, px));
                s.albedo *= 1 + (S3Noise(lp * 40) - 0.5) * 0.15 * S3Detail(0.025, px);
                s.spec = 0.25; s.gloss = 30;
            }
            else if (m == 7)   // wood: planks and grain
            {
                float2 uv = FaceUV(lp, nl);
                float plank = frac(uv.x / 0.16);
                float seam = smoothstep(0, 0.07, plank) * smoothstep(1, 0.93, plank);
                s.albedo *= lerp(1, 0.7 + 0.3 * seam, S3Detail(0.16, px));
                s.albedo *= 1 + (S3Noise(float3(lp.x * 40, lp.y * 3, lp.z * 40)) - 0.5) * 0.35 * S3Detail(0.025, px);
                s.spec = 0.08; s.gloss = 18;
            }
            else if (m == 8)   // glass: lit from inside, reflects the sky
            {
                s.albedo *= 0.88 + 0.18 * S3Noise(lp * 6);
                s.emit += s.albedo * 0.22;
                s.spec = 1.4; s.gloss = 220; s.fresnel = 0.55;
            }
            else if (m == 9)   // adobe: stucco, hairline cracks, sand along the bottom
            {
                float mott = S3Noise(lp * 1.7);
                s.albedo *= 0.9 + 0.16 * mott;
                s.albedo *= 1 + (S3Noise(lp * 27) - 0.5) * 0.12 * S3Detail(0.037, px);
                s.albedo *= 1 + (S3Hash(floor(lp * 110)) - 0.5) * 0.08 * S3Detail(0.009, px);
                float crack = pow(saturate(1 - abs(S3Noise(lp * 3.7 + 5) * 2 - 1)), 30) * step(0.55, S3Noise(lp * 0.9));
                s.albedo *= 1 - crack * 0.45 * S3Detail(0.05, px);
                s.albedo = lerp(s.albedo * 0.84, s.albedo, saturate(lp.y / 0.6 + (mott - 0.5) * 0.6));
            }
            else if (m == 10)   // brick: running bond with mortar
            {
                float id;
                float inside = Blocks(FaceUV(lp, nl), 0.085, 0.21, 0.014, px, id);
                float3 brick = s.albedo * (0.84 + 0.3 * id) * (0.94 + 0.12 * S3Noise(lp * 40));
                s.albedo = lerp(s.albedo, lerp(float3(0.78, 0.72, 0.64), brick, inside), S3Detail(0.085, px));
            }
            else if (m == 11)   // cloth: weave and soft folds
            {
                float2 uv = FaceUV(lp, nl);
                s.albedo *= 1 + sin(uv.x * 520) * sin(uv.y * 520) * 0.07 * S3Detail(0.012, px);
                s.albedo *= 0.86 + 0.22 * S3Noise(lp * 3);
                s.spec = 0.12; s.gloss = 6; s.trans = 0.3;
            }
            else if (m == 12)   // sand: ripples and grains
            {
                float ph = dot(lp.xz, float2(0.8, 0.6)) / 0.3 + S3Noise(lp * 0.8) * 2.5;
                float r = frac(ph);
                float rip = smoothstep(0, 0.7, r) * (1 - smoothstep(0.7, 1, r));
                s.albedo *= 1 + (rip - 0.5) * 0.16 * S3Detail(0.3, px);
                s.albedo *= 1 + (S3Hash(floor(lp * 120)) - 0.5) * 0.1 * S3Detail(0.008, px);
            }
            else if (m == 13)   // snow: soft sheen and glints
            {
                s.albedo *= 0.95 + 0.08 * S3Noise(lp * 4);
                s.spec = 0.18; s.gloss = 10;
                s.sparkle = Glints(lp, 0.022, px, 0.985);
            }
            else if (m == 14)   // ice: glossy, veined, the odd bubble
            {
                s.albedo *= 0.86 + 0.26 * S3Noise(lp * 5 + 1.3);
                s.albedo += step(0.985, S3Hash(floor(lp * 28))) * 0.25 * S3Detail(0.036, px);
                s.spec = 1.2; s.gloss = 140; s.fresnel = 0.5; s.trans = 0.3;
                s.sparkle = Glints(lp, 0.03, px, 0.99);
            }
            else if (m == 15)   // brushed metal
            {
                s.albedo *= 0.9 + 0.2 * S3Noise(float3(lp.x * 120, lp.y * 3, lp.z * 3)) * S3Detail(0.008, px);
                s.spec = 0.9; s.gloss = 60; s.fresnel = 0.25;
            }
            else if (m == 16)   // golf ball: dimples, glossy
            {
                float2 v = S3VoronoiFast(normalize(lp + 1e-5) * 15).xy;
                float dimple = smoothstep(0.12, 0.42, v.x);
                s.albedo *= lerp(1, 0.84 + 0.16 * dimple, S3Detail(0.012, px));
                s.spec = 0.7; s.gloss = 90; s.fresnel = 0.12;
            }
            else if (m == 17)   // toadstool cap: white spots
            {
                float3 c = lp * 22, ci = floor(c);
                float d = length(frac(c) - (0.25 + 0.5 * S3Hash3(ci)));
                float aa = px * 22;
                float spot = (1 - smoothstep(0.17 - aa, 0.17 + aa, d)) * step(0.35, S3Hash(ci + 5));
                s.albedo = lerp(s.albedo, float3(0.97, 0.95, 0.9), spot);
                s.spec = 0.25; s.gloss = 30;
            }
            else if (m == 18)   // palm frond: leaflets
            {
                float2 uv = FaceUV(lp, nl);
                s.albedo *= 1 + sin((uv.x + uv.y) * 90) * 0.1 * S3Detail(0.07, px);
                s.albedo *= 0.88 + 0.24 * S3Noise(lp * 8);
                s.trans = 0.5; s.spec = 0.12; s.gloss = 24;
            }
            else if (m == 19)   // paving: flagstones with dark joints
            {
                float2 uv = FaceUV(lp, nl);
                float2 v = S3Voronoi(float3(uv * 2.4, 0.37));
                float joint = smoothstep(0.03, 0.09 + px * 2.4, v.y - v.x);
                s.albedo *= lerp(1, lerp(0.6, 1, joint) * (0.85 + 0.25 * S3Noise(float3(uv * 2.4, 3))), S3Detail(0.2, px));
                s.albedo *= 1 + (S3Noise(lp * 30) - 0.5) * 0.15 * S3Detail(0.033, px);
            }
            else if (m == 20)   // pine needles: streaks fanning out from the trunk
            {
                float r = length(lp.xz), ang = atan2(lp.z, lp.x);
                float n1 = S3Noise(float3(r * 6, lp.y * 40, ang * 30));
                s.albedo *= lerp(0.95, 0.78 + 0.38 * n1, S3Detail(0.03, px));
                s.albedo *= 1 + (S3Noise(lp * 60) - 0.5) * 0.2 * S3Detail(0.017, px);
                s.trans = 0.25; s.spec = 0.06; s.gloss = 20;
            }
            else if (m == 21)   // palm trunk: ringed
            {
                float ring = frac(lp.y / 0.2 + 0.15 * S3Noise(lp * 3));
                float groove = smoothstep(0, 0.12, ring) * smoothstep(1, 0.8, ring);
                s.albedo *= lerp(1, 0.66 + 0.38 * groove, S3Detail(0.2, px));
                s.albedo *= 1 + (S3Noise(float3(lp.x * 60, lp.y * 15, lp.z * 60)) - 0.5) * 0.2 * S3Detail(0.017, px);
            }
            else if (m == 22)   // sandstone: strata
            {
                s.albedo *= 0.8 + 0.32 * S3Noise(float3(lp.x * 0.6, lp.y * 7, lp.z * 0.6));
                s.albedo *= 1 + (S3Noise(float3(lp.x * 2, lp.y * 22, lp.z * 2)) - 0.5) * 0.2 * S3Detail(0.045, px);
                s.albedo *= 1 + (S3Hash(floor(lp * 80)) - 0.5) * 0.12 * S3Detail(0.0125, px);
            }
            else if (m == 23)   // ice blocks: joints and glints
            {
                float id;
                float inside = Blocks(FaceUV(lp, nl), 0.34, 0.55, 0.03, px, id);
                s.albedo *= lerp(1, lerp(0.8, 0.97 + 0.06 * id, inside), S3Detail(0.34, px));
                s.spec = 0.35; s.gloss = 40; s.fresnel = 0.2;
                s.sparkle = Glints(lp, 0.025, px, 0.99);
            }
            else if (m == 24)   // cactus: ribs and spines
            {
                float2 uv = FaceUV(lp, nl);
                s.albedo *= 1 + sin(uv.x * 60) * 0.12 * S3Detail(0.1, px);
                float3 c = lp * 25, ci = floor(c);
                float spine = (1 - smoothstep(0.08, 0.14, length(frac(c) - 0.5))) * step(0.6, S3Hash(ci));
                s.albedo = lerp(s.albedo, float3(0.95, 0.92, 0.8), spine * S3Detail(0.04, px));
                s.spec = 0.15; s.gloss = 20;
            }
            else if (m == 25)   // petals
            {
                s.albedo *= 0.9 + 0.2 * S3Noise(lp * 40);
                s.trans = 0.5; s.spec = 0.1; s.gloss = 12;
            }
            else if (m == 26)   // paint: smooth and a little glossy
            {
                s.spec = 0.35; s.gloss = 50; s.fresnel = 0.08;
            }
        }

        // ------------------------------------------------------------------ the ground

        // The wind's direction, drifting slowly across the world (shared by all its ripples).
        float3 WindDir(float3 g)
        {
            float3 wn = float3(S3Noise(g * 0.06), S3Noise(g * 0.06 + 9.1), S3Noise(g * 0.06 + 4.7)) - 0.5;
            return normalize(float3(0.82, 0.35, 0.45) + 0.8 * wn);
        }

        // Desert sand ripples and Basin sastrugi: wind-carved ridges, gentle on the windward side and
        // steep on the lee. Returns the ridge profile (0..1) and its gradient in the ground's metres.
        float WindRipples(float3 g, float3 wd, float wavelength, float px, out float3 grad)
        {
            float warp = S3Noise(g * (0.12 / wavelength)) * 2.2 + S3Noise(g * (0.36 / wavelength)) * 0.6;
            float r = frac(dot(g, wd) / wavelength + warp);
            float patchy = smoothstep(0.3, 0.7, S3Noise(g * (0.25 / wavelength) + 13));   // ripples come and go
            float k = patchy * S3Detail(wavelength, px);
            float x = r < 0.7 ? r / 0.7 : (r - 0.7) / 0.3;
            float prof = x * x * (3 - 2 * x), dprof = 6 * x * (1 - x);
            if (r >= 0.7) { prof = 1 - prof; dprof = -dprof / 0.3; } else dprof /= 0.7;
            grad = wd * (dprof / wavelength * k);
            return prof * k;
        }

        // A bump of noise amp * N(g * freq): adds its height and returns its slope along t1, t2.
        float2 NoiseBump(float3 g, float3 t1, float3 t2, float freq, float amp, float eps, inout float h)
        {
            if (amp <= 0) return 0;
            float n0 = S3Noise(g * freq);
            h += amp * n0;
            return amp * float2(S3Noise((g + t1 * eps) * freq) - n0, S3Noise((g + t2 * eps) * freq) - n0) / eps;
        }

        // Small flowers in cells of the ground. Each one is kept inside its cell, so only one cell
        // has to be looked at.
        float3 Flowers(float3 col, float3 g, float3 t1, float3 t2, float px, float density)
        {
            const float cell = 0.42;
            float3 ci = floor(g / cell);
            float3 h = S3Hash3(ci);
            if (h.x > density) return col;
            float3 f = normalize((ci + 0.3 + 0.4 * S3Hash3(ci + 3.7)) * cell) * _S3R;
            float3 inCell = f / cell - ci;
            if (any(inCell < 0.18) || any(inCell > 0.82)) return col;
            float3 v = g - f;
            float2 uv = float2(dot(v, t1), dot(v, t2));
            float r = length(uv);
            float size = 0.028 + 0.03 * h.y;
            float petals = 5 + floor(h.z * 3);
            float shape = size * (0.5 + 0.5 * abs(cos(atan2(uv.y, uv.x) * petals * 0.5 + h.y * 6.28)));
            float aa = px * 1.2;
            float petal = 1 - smoothstep(shape - aa, shape + aa, r);
            float centre = 1 - smoothstep(size * 0.3 - aa, size * 0.3 + aa, r);
            float3 pc = h.z < 0.45 ? float3(0.98, 0.97, 0.94) : h.z < 0.7 ? float3(1.0, 0.85, 0.2)
                      : h.z < 0.86 ? float3(0.74, 0.55, 0.96) : float3(0.98, 0.58, 0.74);
            col = lerp(col, pc, petal);
            return lerp(col, float3(1.0, 0.72, 0.12), centre * S3Detail(size, px));
        }

        float3 MeadowGround(float3 g, float3 t1, float3 t2, float px)
        {
            float n1 = S3Noise(g * 0.13), n2 = S3Noise(g * 0.55), n3 = S3Noise(g * 2.4);
            float3 col = lerp(float3(0.33, 0.60, 0.22), float3(0.55, 0.70, 0.27), smoothstep(0.35, 0.75, n1));
            col = lerp(col, float3(0.26, 0.52, 0.2), smoothstep(0.6, 0.85, n2) * 0.4);
            col *= 0.9 + 0.2 * n3;
            // a lawn of thin blades running every which way, then single blades up close
            float blades = S3Noise(g * float3(38, 14, 27)) * 0.5 + S3Noise(g * float3(15, 41, 21) + 5) * 0.5;
            col *= 1 + (blades - 0.5) * 0.28 * S3Detail(0.04, px);
            col *= 1 + (S3Noise(g * float3(120, 45, 80) + 2) - 0.5) * 0.12 * S3Detail(0.01, px);
            // clover patches: rounder, darker leaves
            float clover = smoothstep(0.7, 0.8, S3Noise(g * 1.7 + 3.3));
            float leaves = lerp(0.5, smoothstep(0.35, 0.65, S3Noise(g * 22)), S3Detail(0.05, px));
            col = lerp(col, float3(0.24, 0.53, 0.21) * (0.85 + 0.25 * leaves), clover * 0.45);
            // worn bare earth here and there
            float bare = smoothstep(0.92, 0.97, S3Noise(g * 0.35 + 21));
            col = lerp(col, float3(0.50, 0.40, 0.28) * (0.85 + 0.3 * S3Noise(g * 9)), bare * 0.55);
            return Flowers(col, g, t1, t2, px, 0.22 * (1 - bare));
        }

        // Desert oases: 1 on the grass, rim fades out over a couple of metres around it.
        void Oases(float4 p, float3 g, out float patch, out float rim)
        {
            float n2 = S3Noise(g * 1.1);
            patch = 0; rim = 0;
            for (int k = 0; k < 16; k++)
            {
                if (k >= (int)_S3PatchCount) break;
                float dm = S3Dist(p, _S3Patch[k]) * _S3R;
                float edge = _S3PatchR[k] * (0.75 + 0.5 * n2);
                patch = max(patch, 1 - smoothstep(edge - 1.0, edge, dm));
                rim = max(rim, 1 - smoothstep(edge, edge + 2.2, dm));
            }
        }

        float3 DesertGround(float4 p, float3 g, float3 t1, float3 t2, float px, float ripple, float patch, float rim)
        {
            float n1 = S3Noise(g * 0.22), n2 = S3Noise(g * 1.1), n3 = S3Noise(g * 5.0);
            float3 sand = lerp(float3(0.88, 0.69, 0.51), float3(0.96, 0.81, 0.63), n1);
            sand *= (0.95 + 0.06 * n3) * (0.97 + 0.05 * ripple);
            sand *= 1 + (S3Hash(floor(g * 110)) - 0.5) * 0.12 * S3Detail(0.009, px);
            // pebbles
            {
                const float cell = 0.33;
                float3 ci = floor(g / cell);
                float3 h = S3Hash3(ci);
                float3 f = (ci + 0.25 + 0.5 * S3Hash3(ci + 1.3)) * cell;
                float d = length(g - normalize(f) * _S3R);
                float size = 0.015 + 0.03 * h.y;
                float peb = (1 - smoothstep(size - px, size + px, d)) * step(h.x, 0.16);
                sand = lerp(sand, lerp(float3(0.45, 0.33, 0.26), float3(0.68, 0.55, 0.45), h.z), peb);
            }
            // oases of lush grass around the registered patches, damp sand and cracked mud around them
            if (rim > 0.01)
            {
                float2 cracks = S3Voronoi(g * 2.2);
                float mud = (1 - smoothstep(0.02, 0.06, cracks.y - cracks.x)) * S3Detail(0.25, px);
                sand = lerp(sand, sand * float3(0.82, 0.76, 0.72), rim * 0.6);
                sand = lerp(sand, float3(0.42, 0.32, 0.25), mud * rim * (1 - patch) * 0.55);
            }
            float3 grass = lerp(float3(0.50, 0.62, 0.20), float3(0.70, 0.75, 0.28), n2) * (0.85 + 0.2 * n3);
            float blades = S3Noise(g * float3(55, 13, 31)) * 0.5 + S3Noise(g * float3(17, 61, 23) + 5) * 0.5;
            grass *= 1 + (blades - 0.5) * 0.45 * S3Detail(0.03, px);
            return lerp(sand, Flowers(grass, g, t1, t2, px, 0.12), patch);
        }

        float3 BasinGround(inout Surface s, float4 p, float3 g, float px, float height)
        {
            float n1 = S3Noise(g * 0.22), n2 = S3Noise(g * 1.1);
            // snow: blue in the hollows, glints in the sun
            float3 snow = lerp(float3(0.80, 0.87, 0.96), float3(0.95, 0.97, 1.0), saturate(height * 14 + 0.1));
            snow *= 0.96 + 0.06 * n2;
            s.sparkle = Glints(g, 0.02, px, 0.982);
            s.spec = 0.15; s.gloss = 10;
            // frozen lakes (the registered patches): deeper blue inwards, cracks, bubbles, dusted with snow
            float lake = 0, depth = 0;
            for (int k = 0; k < 16; k++)
            {
                if (k >= (int)_S3PatchCount) break;
                float dl = S3Dist(p, _S3Patch[k]) * _S3R - _S3PatchR[k] - (n1 - 0.5) * 1.4;
                lake = max(lake, 1 - smoothstep(-0.35, 0, dl));
                depth = max(depth, saturate(-dl / 4));
            }
            if (lake <= 0) return snow;
            float3 ice = lerp(float3(0.62, 0.88, 0.95), float3(0.24, 0.52, 0.74), smoothstep(0, 1, depth));
            ice *= 0.92 + 0.12 * n2;
            float2 c1 = S3Voronoi(g * 0.55), c2 = S3Voronoi(g * 1.9 + 4);
            float crack = max(1 - smoothstep(0.015, 0.045, c1.y - c1.x), (1 - smoothstep(0.01, 0.03, c2.y - c2.x)) * 0.6 * S3Detail(0.3, px));
            ice = lerp(ice, float3(0.86, 0.96, 1.0), crack * 0.8);
            float3 bc = floor(g * 9);
            float bubble = (1 - smoothstep(0.12, 0.2, length(frac(g * 9) - 0.5))) * step(0.93, S3Hash(bc)) * S3Detail(0.11, px);
            ice = lerp(ice, float3(0.9, 0.97, 1.0), bubble * 0.7);
            float dust = smoothstep(0.55, 0.8, S3Noise(g * 0.8 + 7)) * 0.7;
            ice = lerp(ice, snow, dust);
            s.n = lerp(s.n, float4(0, 1, 0, 0), lake);   // the ice is flat
            s.spec = lerp(s.spec, 1.3, lake * (1 - dust));
            s.gloss = lerp(s.gloss, 160, lake);
            s.fresnel = 0.45 * lake * (1 - dust);
            return lerp(snow, ice, lake);
        }

        void GroundSurface(inout Surface s, float4 p, float3 g, float px)
        {
            float3 gn = g * _S3InvR;
            float3 a = abs(gn.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
            float3 t1 = normalize(cross(gn, a)), t2 = cross(gn, t1);

            float dh = S3Dist(p, _S3Hole) * _S3R;   // metres from the cup
            float green = _S3GreenR > 0 ? 1 - smoothstep(_S3GreenR - 0.25, _S3GreenR, dh) : 0;
            float dt = S3Dist(p, _S3Tee) * _S3R;
            float tee = _S3GreenR > 0 ? 1 - smoothstep(1.3, 1.5, dt) : 0;
            float flat = max(green, tee);
            float patch = 0, rim = 0;
            if (_S3Biome > 0.5 && _S3Biome < 1.5)
            {
                Oases(p, g, patch, rim);
                flat = max(flat, patch);   // no sand ripples on the grass
            }

            // relief: its slope tilts the normal (bumps by finite differences, ripples exactly)
            float eps = max(0.008, px);
            float h0 = 0, ripple = 0;
            float2 grad = 0;
            float3 rg;
            if (_S3Biome > 1.5)   // snow: drifts and soft sastrugi
            {
                grad += NoiseBump(g, t1, t2, 0.7, 0.05, eps, h0);
                grad += NoiseBump(g, t1, t2, 9, 0.004 * S3Detail(0.11, px), eps, h0);
                ripple = WindRipples(g, WindDir(g), 0.8, px, rg);
                h0 += 0.01 * ripple;
                grad += 0.01 * float2(dot(rg, t1), dot(rg, t2));
            }
            else if (_S3Biome > 0.5)   // sand: ripples on ripples
            {
                grad += NoiseBump(g, t1, t2, 0.4, 0.03, eps, h0);
                float3 wd = WindDir(g);
                ripple = WindRipples(g, wd, 0.27, px, rg);
                grad += 0.0045 * float2(dot(rg, t1), dot(rg, t2));
                float r2 = WindRipples(g * 1.7 + 3.1, normalize(wd + float3(0.3, -0.5, 0.2)), 0.09, px, rg);
                grad += 0.0012 * 1.7 * float2(dot(rg, t1), dot(rg, t2));
                h0 += 0.0045 * ripple + 0.0012 * r2;
            }
            else   // meadow: tussocks and clumps
            {
                grad += NoiseBump(g, t1, t2, 2.2, 0.025, eps, h0);
                grad += NoiseBump(g, t1, t2, 9, 0.008 * S3Detail(0.11, px), eps, h0);
            }
            grad *= 1 - flat;
            float4 T1 = float4(t1.x, 0, t1.y, t1.z), T2 = float4(t2.x, 0, t2.y, t2.z);
            s.n = normalize(float4(0, 1, 0, 0) - grad.x * T1 - grad.y * T2);

            float3 col;
            if (_S3Biome > 1.5) col = BasinGround(s, p, g, px, h0);
            else if (_S3Biome > 0.5) col = DesertGround(p, g, t1, t2, px, ripple, patch, rim);
            else
            {
                col = MeadowGround(g, t1, t2, px);
                s.spec = 0.05; s.gloss = 14; s.trans = 0.15;
            }

            // tee: a mown patch, a shade lighter
            float3 mown = _S3Biome > 1.5 ? float3(0.86, 0.91, 0.97) : float3(0.47, 0.76, 0.33);
            mown *= 1 + (S3Noise(g * float3(70, 20, 45)) - 0.5) * 0.12 * S3Detail(0.02, px);
            col = lerp(col, mown * 1.03, tee * 0.55);

            // the green: fine grass, mowing stripes, a darker fringe of longer grass at its edge
            float stripe = sin(dot(g, float3(0.71, 0.0, 0.7)) * PI / 1.1);
            float sw = PI / 1.1 * px + 1e-4;
            float3 putting = mown * (1 + 0.055 * smoothstep(-sw, sw, stripe) - 0.03);
            float fringe = smoothstep(_S3GreenR - 0.9, _S3GreenR - 0.1, dh);
            putting = lerp(putting, lerp(putting, col, 0.35) * 0.92, fringe);
            col = lerp(col, putting, green);
            if (green > 0) s.spec = lerp(s.spec, 0.08, green);

            // the cup: a white liner and a dark hole that gets darker inside
            float aa = max(px, 0.004);
            float cup = 1 - smoothstep(_S3CupR - aa, _S3CupR + aa, dh);
            float liner = (1 - smoothstep(_S3CupR + 0.012, _S3CupR + 0.04 + aa, dh)) * (1 - cup);
            col = lerp(col, float3(0.95, 0.95, 0.92), liner * 0.75);
            float3 hole = (_S3Biome > 1.5 ? float3(0.10, 0.14, 0.20) : float3(0.05, 0.06, 0.04)) * lerp(0.4, 1.4, saturate(dh / _S3CupR));
            col = lerp(col, hole, cup);
            if (cup > 0) { s.spec *= 1 - cup; s.sparkle *= 1 - cup; }

            // the golf ball's shadow: a soft blob that spreads and fades as the ball climbs
            if (_S3BallInfo.y > 0.5)
            {
                float4 b = _S3Ball;
                float bh = asin(clamp(b.y, -1, 1)) * _S3R - _S3BallInfo.x;
                float4 bf = normalize(float4(b.x, 0, b.z, b.w));
                float db = S3Dist(p, bf) * _S3R;
                float r = _S3BallInfo.x * (0.75 + 0.25 * bh);
                float shadow = (1 - smoothstep(r * 0.3, r * 1.3, db)) * 0.55 * saturate(1 - bh / 7);
                col *= 1 - shadow;
            }
            s.albedo *= col;
        }

        // ------------------------------------------------------------------ lighting

        float4 S3Frag(Varyings i) : SV_Target
        {
            SeeThrough(i);
            // derivatives first (they must not sit in a branch)
            float3 lp = i.lp;
            float3 dlx = ddx(lp), dly = ddy(lp);
            float3 nl = cross(dly, dlx);
            nl = dot(nl, nl) > 1e-20 ? normalize(nl) : float3(0, 1, 0);
            float pxLocal = length(fwidth(lp));
            float4 p = normalize(i.p4);
            float3 g = float3(p.x, p.z, p.w) * _S3R;   // ground coordinates in metres
            float pxGround = length(fwidth(g));

            float4 n = normalize(i.n4 - dot(i.n4, p) * p);
            float4 up = float4(0, 1, 0, 0) - p.y * p;
            float upLen = length(up);
            up = upLen > 1e-5 ? up / upLen : n;
            float upness = dot(n, up);
            float4 toEye = _S3Eye - dot(_S3Eye, p) * p;
            float eyeLen = length(toEye);
            toEye = eyeLen > 1e-6 ? toEye / eyeLen : n;
            if (dot(n, toEye) < 0) n = -n;          // two-sided: always light the face we see

            Surface s;
            s.albedo = i.color.rgb * _Color.rgb;
            s.n = n; s.spec = 0.04; s.gloss = 12; s.trans = 0; s.fresnel = 0; s.sparkle = 0;
            s.emit = 0;
            if (_Ground > 0.5) GroundSurface(s, p, g, pxGround);
            else Material(s, i.mat, lp, nl, pxLocal, upness);
            n = s.n;

            float4 L = _S3Sun - dot(_S3Sun, p) * p;
            float lenL = length(L);
            L = lenL > 1e-5 ? L / lenL : n;
            float ndl = dot(n, L);
            float wrap = s.trans * 0.4;
            float diff = saturate((ndl + wrap) / (1 + wrap));
            float hemi = dot(n, up) * 0.5 + 0.5;
            float3 sun = _S3SunColor.rgb;

            // sky and bounce light, plus a little warm fill from the sun's side of the sky so shade isn't grey
            float3 ambient = lerp(_S3GroundAmbient.rgb, _S3SkyAmbient.rgb, hemi) + sun * (0.08 + 0.06 * saturate(ndl + 0.6));
            float3 col = s.albedo * (ambient + sun * diff);
            // light shining through leaves and blades seen against the sun
            float behind = saturate(-ndl);
            col += s.albedo * sun * s.trans * behind * (0.55 + 0.6 * pow(saturate(dot(-toEye, L)), 4));
            // highlights and glints
            float4 h = L + toEye;
            float hl = length(h);
            h = hl > 1e-5 ? h / hl : n;
            float spec = pow(saturate(dot(n, h)), s.gloss) * s.spec * (s.gloss + 8) / 25;
            col += sun * spec * saturate(ndl * 4);
            col += sun * s.sparkle * 2.2 * saturate(ndl * 3);
            // the sky reflected at grazing angles (glass, ice)
            float fres = pow(1 - saturate(dot(n, toEye)), 5) * s.fresnel + s.fresnel * 0.08;
            float3 refl = lerp(_S3Haze.rgb, _S3SkyColor.rgb * 1.08, saturate(dot(reflect(-toEye, n), up) * 1.5 + 0.4));
            col = lerp(col, refl, saturate(fres));
            // soft rim of sunlight on silhouettes
            float rim = pow(1 - saturate(dot(n, toEye)), 4);
            col += s.albedo * sun * rim * 0.25 * (0.3 + 0.7 * diff);
            // emissive: vertex alpha below 1 (lenses, crystals), the material's own, and a per-renderer glow
            col += s.albedo * ((1 - i.color.a) * 1.6 + _Glow) + s.emit;

            col = lerp(i.air.rgb + S3SunGlow(p), col, i.air.a);
            // colours above are authored in sRGB; the project renders in linear space
            return float4(S3SRGBToLinear(col), 1);
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
