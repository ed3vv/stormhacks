using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly props for the two maps:
///   * Meadow: houses, round and pine trees, rocks, mushrooms and grass, in the style of Hyperbolica.
///   * Desert: an adobe fort on a stone platform, palms, grass oases, boulders, dunes and ruins,
///     after the golden-hour desert concept.
/// Each prop is authored in plain local metres, placed on the ground great-sphere by a frame,
/// and baked into one shared scenery mesh. Props also register their collision cylinder, a baked
/// contact shadow and (if big) a see-through slot with the world.
/// </summary>
public static class S3Scenery
{
    static readonly Color Wall = new Color(0.60f, 0.47f, 0.38f);
    static readonly Color WallEdge = new Color(0.45f, 0.34f, 0.27f);
    static readonly Color Roof = new Color(0.86f, 0.10f, 0.08f);
    static readonly Color RoofEdge = new Color(0.62f, 0.06f, 0.05f);
    static readonly Color Pane = new Color(0.98f, 0.88f, 0.36f);
    static readonly Color Frame = new Color(0.26f, 0.21f, 0.18f);
    static readonly Color Door = new Color(0.62f, 0.63f, 0.66f);
    static readonly Color Chimney = new Color(0.40f, 0.38f, 0.38f);
    static readonly Color Bark = new Color(0.42f, 0.29f, 0.19f);
    static readonly Color Stone = new Color(0.78f, 0.78f, 0.76f);

    // desert palette (from the concept: warm sandstone, terracotta, red cloth, green trim)
    static readonly Color Adobe = new Color(0.90f, 0.74f, 0.56f);
    static readonly Color AdobeShade = new Color(0.82f, 0.64f, 0.47f);
    static readonly Color Paving = new Color(0.80f, 0.67f, 0.53f);
    static readonly Color Brick = new Color(0.70f, 0.42f, 0.30f);
    static readonly Color Terracotta = new Color(0.74f, 0.40f, 0.26f);
    static readonly Color Trim = new Color(0.58f, 0.30f, 0.22f);
    static readonly Color Opening = new Color(0.24f, 0.16f, 0.13f);
    static readonly Color Wood = new Color(0.44f, 0.28f, 0.18f);
    static readonly Color TrimGreen = new Color(0.36f, 0.58f, 0.46f);
    static readonly Color Cloth = new Color(0.86f, 0.30f, 0.18f);
    static readonly Color Gate = new Color(0.38f, 0.36f, 0.36f);
    static readonly Color Sand = new Color(0.92f, 0.76f, 0.58f);

    struct KeepOut { public Vector4 p; public float r; public KeepOut(Vector4 p, float r) { this.p = p; this.r = r; } }

    public static MeshBuilder Build(S3World world, System.Random rng) =>
        world.biome == S3World.Biome.Desert ? BuildDesert(world, rng) : BuildMeadow(world, rng);

    /// <summary>
    /// Keep tees and greens clear of props. The line between them is deliberately left alone:
    /// trees and buildings in the way are part of each hole.
    /// </summary>
    static List<KeepOut> CourseKeepOut(S3World world)
    {
        var keep = new List<KeepOut>();
        foreach (var h in world.holes)
        {
            keep.Add(new KeepOut(h.tee, 3f));
            keep.Add(new KeepOut(h.cup, world.greenRadius + 1f));
        }
        return keep;
    }

    /// <summary>Bakes a prop and registers what it needs with the world.</summary>
    static void Add(S3World world, MeshBuilder scene, MeshBuilder prop, Matrix4x4 frame,
                    float fadeRadius = 0, float fadeHeight = 0, float sway = 0, float occlusion = 0.35f,
                    float obstacleRadius = 0, float obstacleHeight = 0,
                    float shadowRadius = 0, float shadowHeight = 0, float shadowStrength = 1)
    {
        Vector4 foot = frame.GetColumn(3);
        int fade = fadeRadius > 0 ? world.RegisterFadeProp(foot, fadeRadius, fadeHeight) : 0;
        scene.AppendBaked(prop, frame, world.radius, fade, sway, occlusion);
        if (obstacleRadius > 0) world.obstacles.Add(new S3World.Obstacle(foot, obstacleRadius, obstacleHeight));
        if (shadowRadius > 0) world.RegisterShadow(foot, shadowRadius, shadowHeight, shadowStrength);
    }

    // ================================================================== meadow

    static MeshBuilder BuildMeadow(S3World world, System.Random rng)
    {
        float R = world.radius;
        var scene = new MeshBuilder();
        var keep = CourseKeepOut(world);

        // houses (fixed spots)
        var houses = new[]
        {
            new Vector4(7, 12, 200, 0),     // x, z, yaw (deg), style
            new Vector4(-12, -3, 75, 1),
            new Vector4(11, -11, -35, 2),
            new Vector4(-21, -14, 130, 0),  // well past the equator: watch it grow as you walk away
        };
        foreach (var hd in houses)
        {
            var frame = S3.GroundFrame(hd.x, hd.y, hd.z * Mathf.Deg2Rad, R);
            var mb = new MeshBuilder();
            float w = 4.2f + hd.w * 0.5f, d = 5f + hd.w * 0.4f, h = 2.8f + hd.w * 0.3f, rh = 1.9f;
            House(mb, w, d, h, rh);
            float footprint = 0.5f * Mathf.Sqrt(w * w + d * d);
            Add(world, scene, mb, frame, footprint, h + rh + 0.7f, 0, 0.3f, footprint * 0.9f, h + rh, footprint * 0.95f, h + rh);
            keep.Add(new KeepOut(frame.GetColumn(3), footprint + 1.5f));
        }

        // trees
        for (int i = 0; i < 34; i++)
        {
            if (!TryPlace(rng, R, keep, 1.8f, out var frame)) continue;
            var mb = new MeshBuilder();
            float scale = 0.8f + 0.5f * (float)rng.NextDouble();
            if (rng.NextDouble() < 0.65) RoundTree(mb, rng, scale); else PineTree(mb, rng, scale);
            Add(world, scene, mb, frame, 1.5f * scale, 4.8f * scale, 0.25f, 0.4f, 0.3f * scale, 6f, 1.4f * scale, 4f * scale);
            keep.Add(new KeepOut(frame.GetColumn(3), 1.6f * scale));
        }

        // rocks
        for (int i = 0; i < 22; i++)
        {
            float size = 0.35f + 1.1f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f);
            float g = 0.72f + 0.12f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.35f, 0), new Vector3(size, size * 0.7f, size * 0.9f), 1,
                         new Color(g, g, g * 0.98f), rng, 0.18f);
            Add(world, scene, mb, frame, size, size, 0, 0.3f, size * 0.85f, size, size * 0.9f, size, 0.7f);
            keep.Add(new KeepOut(frame.GetColumn(3), size + 0.3f));
        }

        // mushrooms
        for (int i = 0; i < 26; i++)
        {
            if (!TryPlace(rng, R, keep, 0.4f, out var frame)) continue;
            var mb = new MeshBuilder();
            int n = 1 + rng.Next(3);
            for (int k = 0; k < n; k++)
            {
                var o = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.5f;
                float s = 0.7f + 0.6f * (float)rng.NextDouble();
                mb.Frustum(o, 0.05f * s, 0.04f * s, 0.22f * s, 6, new Color(0.95f, 0.92f, 0.85f));
                mb.Frustum(o + new Vector3(0, 0.2f * s, 0), 0.17f * s, 0.0f, 0.13f * s, 7, new Color(0.88f, 0.12f, 0.1f));
            }
            Add(world, scene, mb, frame, occlusion: 0.2f);
        }

        // grass tufts (all over the world)
        for (int i = 0; i < 1100; i++)
        {
            if (!TryPlace(rng, R, keep, 0.2f, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            Tuft(mb, rng);
            Add(world, scene, mb, frame, sway: 1f, occlusion: 0.45f);
        }
        return scene;
    }

    // ================================================================== desert

    static MeshBuilder BuildDesert(S3World world, System.Random rng)
    {
        float R = world.radius;
        var scene = new MeshBuilder();
        var keep = CourseKeepOut(world);
        var oases = new List<(Matrix4x4 frame, float radius)>();

        // the fort on its platform, in a grassy oasis (like the concept)
        {
            var frame = S3.GroundFrame(-10, 13, -35 * Mathf.Deg2Rad, R);
            var mb = new MeshBuilder();
            Fort(mb);
            Add(world, scene, mb, frame, 6.2f, 10f, 0, 0.25f, 5.6f, 9.5f, 5.5f, 7f);
            Vector4 foot = frame.GetColumn(3);
            world.RegisterPatch(foot, 9.5f);
            oases.Add((frame, 9.5f));
            keep.Add(new KeepOut(foot, 7.5f));
        }

        // smaller adobe houses, one far past the equator so it looms
        foreach (var hd in new[] { new Vector3(13, -12, 150), new Vector3(-22, -15, 40), new Vector3(20, 9, -120) })
        {
            var frame = S3.GroundFrame(hd.x, hd.y, hd.z * Mathf.Deg2Rad, R);
            var mb = new MeshBuilder();
            AdobeHouse(mb, rng);
            Add(world, scene, mb, frame, 3.2f, 5.5f, 0, 0.25f, 2.8f, 4.5f, 3f, 4f);
            keep.Add(new KeepOut(frame.GetColumn(3), 4.5f));
        }

        // grass oases: around every green, and a few scattered ones
        foreach (var h in world.holes)
        {
            world.RegisterPatch(h.cup, world.greenRadius + 2.5f);
            oases.Add((S3.FrameAt(h.cup, S3.EY, S3.EZ), world.greenRadius + 2.5f));
        }
        for (int i = 0; i < 7; i++)
        {
            float r = 3.5f + 2.5f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, keep, 2f, out var frame)) continue;
            world.RegisterPatch(frame.GetColumn(3), r);
            oases.Add((frame, r));
        }

        // palms: clustered at the oases, plus a few lone ones
        foreach (var (center, r) in oases)
        {
            int n = 1 + rng.Next(3);
            for (int k = 0; k < n; k++)
                if (TryPlaceNear(rng, R, center, r * 0.5f, r * 0.95f, keep, 1.2f, out var frame)) PlacePalm(world, scene, rng, frame, keep);
        }
        for (int i = 0; i < 9; i++)
            if (TryPlace(rng, R, keep, 1.5f, out var frame)) PlacePalm(world, scene, rng, frame, keep);

        // dunes: big soft mounds, kept well away from the course
        for (int i = 0; i < 10; i++)
        {
            float rx = 4.5f + 3f * (float)rng.NextDouble(), rz = 3f + 1.8f * (float)rng.NextDouble(), ry = 1.1f + 0.9f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, keep, rx + 1.5f, out var frame)) continue;
            var mb = new MeshBuilder(1.2f);
            float g = (float)rng.NextDouble() * 0.04f;
            mb.Icosphere(new Vector3(0, -0.35f * ry, 0), new Vector3(rx, ry, rz), 2, new Color(Sand.r + g, Sand.g + g, Sand.b + g), rng, 0.04f);
            Add(world, scene, mb, frame, rx, ry, 0, 0, Mathf.Min(rx, rz) * 0.7f, ry * 0.6f);
            keep.Add(new KeepOut(frame.GetColumn(3), rx));
        }

        // boulders (dark, like the concept's foreground rocks)
        for (int i = 0; i < 18; i++)
        {
            float size = 0.5f + 1.4f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f);
            float g = 0.38f + 0.1f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.3f, 0), new Vector3(size * 1.2f, size * 0.8f, size), 1,
                         new Color(g * 1.12f, g, g * 0.92f), rng, 0.22f);
            Add(world, scene, mb, frame, size * 1.1f, size, 0, 0.3f, size * 0.95f, size, size, size, 0.8f);
            keep.Add(new KeepOut(frame.GetColumn(3), size + 0.4f));
        }

        // ruined walls
        for (int i = 0; i < 6; i++)
        {
            if (!TryPlace(rng, R, keep, 2.2f, out var frame)) continue;
            var mb = new MeshBuilder();
            RuinWall(mb, rng);
            Add(world, scene, mb, frame, 2.2f, 2f, 0, 0.3f, 1.6f, 1.8f, 1.8f, 1.6f, 0.8f);
            keep.Add(new KeepOut(frame.GetColumn(3), 2.4f));
        }

        // tall grass in the oases, dry tufts on the sand
        foreach (var (center, r) in oases)
            for (int k = 0; k < (int)(r * r * 0.9f); k++)
            {
                if (!TryPlaceNear(rng, R, center, 0, r * 0.9f, keep, 0.15f, out var frame, attempts: 3)) continue;
                var mb = new MeshBuilder(1f);
                TallGrass(mb, rng);
                Add(world, scene, mb, frame, sway: 1f, occlusion: 0.5f);
            }
        for (int i = 0; i < 320; i++)
        {
            if (!TryPlace(rng, R, keep, 0.2f, out var frame, attempts: 3)) continue;
            var mb = new MeshBuilder(1f);
            DryTuft(mb, rng);
            Add(world, scene, mb, frame, sway: 0.7f, occlusion: 0.35f);
        }
        return scene;
    }

    static void PlacePalm(S3World world, MeshBuilder scene, System.Random rng, Matrix4x4 frame, List<KeepOut> keep)
    {
        var mb = new MeshBuilder();
        float s = 0.85f + 0.4f * (float)rng.NextDouble();
        float height = Palm(mb, rng, s);
        Add(world, scene, mb, frame, 2.4f * s, height + 1f, 0.3f, 0.3f, 0.3f * s, height, 2f * s, height, 0.75f);
        keep.Add(new KeepOut(frame.GetColumn(3), 1.2f * s));
    }

    // ================================================================== placement

    static bool TryPlace(System.Random rng, float R, List<KeepOut> keep, float radius, out Matrix4x4 frame, int attempts = 40)
    {
        for (int a = 0; a < attempts; a++)
        {
            // uniform on the ground sphere: cos(distance) is uniform
            float dist = Mathf.Acos(1 - 2 * (float)rng.NextDouble()) * R;
            float theta = 2 * Mathf.PI * (float)rng.NextDouble();
            frame = S3.GroundFrame(dist * Mathf.Cos(theta), dist * Mathf.Sin(theta), 2 * Mathf.PI * (float)rng.NextDouble(), R);
            if (Clear(frame.GetColumn(3), R, keep, radius)) return true;
        }
        frame = Matrix4x4.identity;
        return false;
    }

    /// <summary>Somewhere between minDist and maxDist metres from center's foot.</summary>
    static bool TryPlaceNear(System.Random rng, float R, Matrix4x4 center, float minDist, float maxDist,
                             List<KeepOut> keep, float radius, out Matrix4x4 frame, int attempts = 20)
    {
        for (int a = 0; a < attempts; a++)
        {
            float dist = Mathf.Lerp(minDist, maxDist, Mathf.Sqrt((float)rng.NextDouble()));
            float theta = 2 * Mathf.PI * (float)rng.NextDouble();
            frame = center * S3.GroundFrame(dist * Mathf.Cos(theta), dist * Mathf.Sin(theta), 2 * Mathf.PI * (float)rng.NextDouble(), R);
            if (Clear(frame.GetColumn(3), R, keep, radius)) return true;
        }
        frame = Matrix4x4.identity;
        return false;
    }

    static bool Clear(Vector4 foot, float R, List<KeepOut> keep, float radius)
    {
        foreach (var k in keep)
            if (S3.Dist(foot, k.p) * R < k.r + radius) return false;
        return true;
    }

    // ------------------------------------------------------------------ props

    /// <summary>
    /// Gabled house: walls w (x) by d (z), wall height h, roof rise rh. Front faces -z.
    /// Everything is solid: thick walls on a stone plinth, prism gables, thick roof slabs with a
    /// ridge cap, and windows and a door that stand out of the walls with real depth.
    /// </summary>
    static void House(MeshBuilder mb, float w, float d, float h, float rh)
    {
        const float T = 0.28f;   // wall thickness
        float x = w / 2, z = d / 2;
        // plinth
        mb.Box(new Vector3(-x - 0.1f, 0, -z - 0.1f), new Vector3(x + 0.1f, 0.32f, z + 0.1f), new Color(0.62f, 0.60f, 0.57f));
        // walls (outer faces at +-x, +-z)
        mb.Box(new Vector3(-x, 0, -z), new Vector3(x, h, -z + T), Wall);
        mb.Box(new Vector3(-x, 0, z - T), new Vector3(x, h, z), Wall);
        mb.Box(new Vector3(-x, 0, -z + T), new Vector3(-x + T, h, z - T), WallEdge);
        mb.Box(new Vector3(x - T, 0, -z + T), new Vector3(x, h, z - T), WallEdge);
        // gables as prisms, flush with the front and back walls
        mb.Prism(new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(0, h + rh, -z), new Vector3(0, 0, T), Wall);
        mb.Prism(new Vector3(-x, h, z - T), new Vector3(x, h, z - T), new Vector3(0, h + rh, z - T), new Vector3(0, 0, T), Wall);
        // roof: two thick slabs whose planes meet the wall tops exactly, plus a ridge cap
        float o = 0.45f, eave = h - rh * o / x, zo = z + o;
        mb.Slab(new Vector3(-x - o, eave, -zo), new Vector3(-x - o, eave, zo), new Vector3(0, h + rh, zo), new Vector3(0, h + rh, -zo), 0.2f, Roof, RoofEdge);
        mb.Slab(new Vector3(x + o, eave, zo), new Vector3(x + o, eave, -zo), new Vector3(0, h + rh, -zo), new Vector3(0, h + rh, zo), 0.2f, Roof, RoofEdge);
        mb.Box(new Vector3(-0.14f, h + rh + 0.12f, -zo - 0.04f), new Vector3(0.14f, h + rh + 0.3f, zo + 0.04f), RoofEdge);
        // chimney with a cap
        mb.Box(new Vector3(x * 0.35f, h + rh * 0.4f, z * 0.3f), new Vector3(x * 0.35f + 0.5f, h + rh + 0.7f, z * 0.3f + 0.5f), Chimney);
        mb.Box(new Vector3(x * 0.35f - 0.06f, h + rh + 0.7f, z * 0.3f - 0.06f), new Vector3(x * 0.35f + 0.56f, h + rh + 0.82f, z * 0.3f + 0.56f), Frame);

        Vector3 up = Vector3.up;
        // a framed window standing out of the wall, with mullions and a sill; c is on the outer wall face
        void Window(Vector3 c, Vector3 outward, Vector3 side, float ww, float wh)
        {
            mb.OrientedBox(c + outward * 0.04f, side, up, outward, new Vector3(ww / 2 + 0.09f, wh / 2 + 0.09f, 0.04f), Frame);
            Vector3 pc = c + outward * 0.083f;
            mb.Quad(pc - side * (ww / 2) - up * (wh / 2), pc + side * (ww / 2) - up * (wh / 2),
                    pc + side * (ww / 2) + up * (wh / 2), pc - side * (ww / 2) + up * (wh / 2), Pane);
            mb.OrientedBox(c + outward * 0.1f, side, up, outward, new Vector3(0.03f, wh / 2, 0.02f), Frame);
            mb.OrientedBox(c + outward * 0.1f, side, up, outward, new Vector3(ww / 2, 0.03f, 0.02f), Frame);
            mb.OrientedBox(c - up * (wh / 2 + 0.13f) + outward * 0.09f, side, up, outward, new Vector3(ww / 2 + 0.17f, 0.05f, 0.09f), WallEdge);
        }
        float wy = Mathf.Min(1.65f, h - 0.8f);
        // front: door (frame, slab, knob, step) + window
        Vector3 doorC = new Vector3(-x * 0.45f, 1.05f, -z);
        mb.OrientedBox(doorC + Vector3.back * 0.035f, Vector3.right, up, Vector3.back, new Vector3(0.56f, 1.08f, 0.035f), Frame);
        mb.OrientedBox(doorC + Vector3.back * 0.09f + up * -0.03f, Vector3.right, up, Vector3.back, new Vector3(0.44f, 1.0f, 0.03f), Door);
        mb.Icosphere(doorC + new Vector3(0.3f, -0.05f, -0.15f), Vector3.one * 0.05f, 0, new Color(0.85f, 0.7f, 0.3f));
        mb.Box(new Vector3(doorC.x - 0.7f, 0, -z - 0.55f), new Vector3(doorC.x + 0.7f, 0.18f, -z), new Color(0.62f, 0.60f, 0.57f));
        Window(new Vector3(x * 0.45f, wy, -z), Vector3.back, Vector3.right, 0.9f, 0.9f);
        Window(new Vector3(0, h + rh * 0.35f, -z), Vector3.back, Vector3.right, 0.55f, 0.55f);
        // back
        Window(new Vector3(-x * 0.45f, wy, z), Vector3.forward, Vector3.left, 0.9f, 0.9f);
        Window(new Vector3(x * 0.45f, wy, z), Vector3.forward, Vector3.left, 0.9f, 0.9f);
        // sides
        for (int s = -1; s <= 1; s += 2)
        {
            Window(new Vector3(-x, wy, s * z * 0.45f), Vector3.left, Vector3.back, 0.8f, 0.9f);
            Window(new Vector3(x, wy, s * z * 0.45f), Vector3.right, Vector3.forward, 0.8f, 0.9f);
        }
    }

    static void RoundTree(MeshBuilder mb, System.Random rng, float s)
    {
        float trunkH = 2.0f * s;
        mb.Frustum(Vector3.zero, 0.24f * s, 0.15f * s, trunkH + 0.4f * s, 6, Bark);
        float g = (float)rng.NextDouble();
        var leaf = new Color(0.20f + 0.08f * g, 0.48f + 0.12f * g, 0.16f);
        mb.Icosphere(new Vector3(0, trunkH + 0.9f * s, 0), new Vector3(1.35f, 1.15f, 1.35f) * s, 1, leaf, rng, 0.12f);
        mb.Icosphere(new Vector3(0.45f * s, trunkH + 1.7f * s, 0.2f * s), new Vector3(0.85f, 0.8f, 0.85f) * s, 1, leaf * 1.08f, rng, 0.12f);
        mb.Icosphere(new Vector3(-0.5f * s, trunkH + 0.5f * s, -0.3f * s), new Vector3(0.8f, 0.7f, 0.8f) * s, 1, leaf * 0.92f, rng, 0.12f);
    }

    static void PineTree(MeshBuilder mb, System.Random rng, float s)
    {
        mb.Frustum(Vector3.zero, 0.2f * s, 0.12f * s, 1.2f * s, 6, Bark);
        float g = (float)rng.NextDouble();
        var leaf = new Color(0.12f + 0.05f * g, 0.38f + 0.1f * g, 0.20f);
        for (int k = 0; k < 3; k++)
        {
            float y = (0.9f + 1.0f * k) * s, r = (1.5f - 0.38f * k) * s;
            mb.Frustum(new Vector3(0, y, 0), r, 0, 1.8f * s, 7, leaf * (1 + 0.06f * k), k * 0.4f);
        }
    }

    static void Tuft(MeshBuilder mb, System.Random rng)
    {
        int blades = 3 + rng.Next(3);
        float g = (float)rng.NextDouble();
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0, dir.x);
            Vector3 b = dir * 0.05f * (float)rng.NextDouble();
            float h = 0.2f + 0.25f * (float)rng.NextDouble();
            Vector3 tip = b + dir * (0.08f + 0.1f * (float)rng.NextDouble()) + Vector3.up * h;
            var col = new Color(0.25f + 0.1f * g, 0.55f + 0.12f * g, 0.18f);
            mb.Tri(b - side * 0.035f, b + side * 0.035f, tip, col);
        }
    }

    // ------------------------------------------------------------------ desert props

    /// <summary>
    /// Adobe fort after the concept art: a stone platform reached by terracotta-walled stairs,
    /// a main block with a green trim beam and red awning, a crenellated tower draped with red
    /// cloth, a lower side wing, planters and a flagstone path. Front faces -z.
    /// </summary>
    static void Fort(MeshBuilder mb)
    {
        const float P = 1.0f;          // platform height
        const float B = P + 0.15f;     // floor of the buildings
        // platform with a brick lip
        mb.Box(new Vector3(-5, 0, -3), new Vector3(5, P, 5), Paving);
        mb.Box(new Vector3(-5.15f, P, -3.15f), new Vector3(5.15f, B, 5.15f), Brick);

        // stairs down to the front, with stepped terracotta side walls and pillars
        const int steps = 6;
        for (int k = 0; k < steps; k++)
        {
            float y = B * (steps - k) / steps, z1 = -3.15f - 0.5f * k, z0 = z1 - 0.5f;
            mb.Box(new Vector3(-1.3f, 0, z0), new Vector3(1.3f, y, z1), Paving);
            mb.Box(new Vector3(-1.65f, 0, z0), new Vector3(-1.3f, y + 0.3f, z1), Terracotta);
            mb.Box(new Vector3(1.3f, 0, z0), new Vector3(1.65f, y + 0.3f, z1), Terracotta);
        }
        mb.Box(new Vector3(-1.75f, B, -3.4f), new Vector3(-1.2f, B + 0.75f, -2.85f), Terracotta);
        mb.Box(new Vector3(1.2f, B, -3.4f), new Vector3(1.75f, B + 0.75f, -2.85f), Terracotta);
        Planter(mb, new Vector3(-4.0f, B, -2.2f));
        Planter(mb, new Vector3(4.0f, B, -2.2f));

        // flagstone path leading to the stairs
        for (int i = 0; i < 7; i++)
        {
            float z = -6.6f - 0.75f * i, x = (i % 2 == 0 ? -0.45f : 0.4f) + 0.15f * Mathf.Sin(i * 2.3f);
            var c = Color.Lerp(Paving, Sand, 0.3f + 0.1f * (i % 3));
            mb.Box(new Vector3(x - 0.4f, 0, z - 0.3f), new Vector3(x + 0.4f, 0.05f, z + 0.3f), c);
            mb.Box(new Vector3(-x - 0.3f, 0, z - 0.6f), new Vector3(-x + 0.25f, 0.05f, z - 0.15f), c);
        }

        // main block, tower and side wing
        mb.Box(new Vector3(-3.6f, B, 0), new Vector3(1.6f, B + 3.3f, 4.2f), Adobe);
        mb.Box(new Vector3(-1.0f, B, 0.6f), new Vector3(2.0f, B + 7.6f, 3.6f), Adobe);
        mb.Box(new Vector3(1.6f, B, 0.8f), new Vector3(4.4f, B + 2.5f, 4.2f), AdobeShade);
        // cornices
        mb.Box(new Vector3(-3.75f, B + 3.3f, -0.15f), new Vector3(1.75f, B + 3.5f, 4.35f), Trim);
        mb.Box(new Vector3(-1.15f, B + 7.6f, 0.45f), new Vector3(2.15f, B + 7.85f, 3.75f), Trim);
        mb.Box(new Vector3(1.45f, B + 2.5f, 0.65f), new Vector3(4.55f, B + 2.68f, 4.35f), Trim);
        mb.Box(new Vector3(-1.0f, B + 5.2f, 0.5f), new Vector3(2.0f, B + 5.35f, 0.6f), Trim);   // band on the tower
        Crenels(mb, -1.1f, 2.1f, 0.5f, 3.7f, B + 7.85f, 0.32f, 0.45f, Brick);
        Crenels(mb, -3.7f, -1.05f, -0.1f, 4.3f, B + 3.5f, 0.28f, 0.35f, Brick);

        // trim beam, awning and draped cloth
        mb.Box(new Vector3(-3.6f, B + 2.05f, -0.12f), new Vector3(1.6f, B + 2.2f, 0), TrimGreen);
        mb.Box(new Vector3(-3.5f, B + 2.5f, -1.05f), new Vector3(-0.3f, B + 2.6f, -0.95f), TrimGreen);
        mb.Quad(new Vector3(-3.4f, B + 2.95f, 0), new Vector3(-0.4f, B + 2.95f, 0),
                new Vector3(-0.4f, B + 2.5f, -1.0f), new Vector3(-3.4f, B + 2.5f, -1.0f), Cloth);
        for (int i = 0; i < 6; i++)   // scalloped edge
        {
            float x0 = -3.4f + 0.5f * i, x1 = x0 + 0.5f;
            mb.Tri(new Vector3(x0, B + 2.5f, -1.0f), new Vector3(x1, B + 2.5f, -1.0f), new Vector3((x0 + x1) / 2, B + 2.25f, -1.02f), Cloth);
        }
        mb.Quad(new Vector3(-1.0f, B + 7.5f, 0.7f), new Vector3(-1.0f, B + 7.5f, 2.6f),
                new Vector3(-3.3f, B + 3.5f, 3.4f), new Vector3(-3.3f, B + 3.5f, 0.3f), Cloth);

        // vigas (roof beams poking out of the facade)
        for (float x = -3.3f; x < -1.1f; x += 0.55f)
            mb.Box(new Vector3(x, B + 3.0f, -0.35f), new Vector3(x + 0.14f, B + 3.14f, 0), Wood);

        // openings
        Vector3 right = Vector3.right, front = Vector3.back;
        mb.Arch(new Vector3(0.4f, B, 0), right, front, 1.2f, 2.3f, Wood);              // door
        mb.Arch(new Vector3(-2.4f, B, 0), right, front, 1.6f, 2.0f, Gate);             // barred gate
        for (float x = -3.0f; x < -1.7f; x += 0.3f)
            mb.Box(new Vector3(x, B, -0.07f), new Vector3(x + 0.05f, B + 1.75f, -0.03f), Opening);
        mb.Arch(new Vector3(0.5f, B + 5.9f, 0.6f), right, front, 0.7f, 1.2f, Opening);  // tower windows
        mb.Arch(new Vector3(-0.3f, B + 3.9f, 0.6f), right, front, 0.4f, 0.7f, Opening);
        mb.Arch(new Vector3(1.3f, B + 3.9f, 0.6f), right, front, 0.4f, 0.7f, Opening);
        mb.Arch(new Vector3(2.0f, B + 4.8f, 2.1f), Vector3.forward, Vector3.right, 0.55f, 0.95f, Opening);
        mb.Arch(new Vector3(3.0f, B + 0.6f, 0.8f), right, front, 0.7f, 1.2f, Opening); // wing
        foreach (float x in new[] { -3.0f, -2.2f })
            mb.Quad(new Vector3(x - 0.2f, B + 2.75f, -0.03f), new Vector3(x + 0.2f, B + 2.75f, -0.03f),
                    new Vector3(x + 0.2f, B + 3.2f, -0.03f), new Vector3(x - 0.2f, B + 3.2f, -0.03f), Opening);
    }

    /// <summary>Square terracotta planter with a tuft of green.</summary>
    static void Planter(MeshBuilder mb, Vector3 at)
    {
        mb.Frustum(at, 0.5f, 0.62f, 1.1f, 4, Terracotta, Mathf.PI / 4);
        mb.Frustum(at + Vector3.up * 1.1f, 0.68f, 0.68f, 0.12f, 4, Trim, Mathf.PI / 4);
        mb.Frustum(at + Vector3.up * 1.2f, 0.48f, 0.0f, 0.5f, 6, new Color(0.38f, 0.55f, 0.2f));
    }

    /// <summary>Merlons along the edges of the rectangle [x0,x1] x [z0,z1] at height y.</summary>
    static void Crenels(MeshBuilder mb, float x0, float x1, float z0, float z1, float y, float size, float height, Color col)
    {
        void Run(Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(2, Mathf.RoundToInt(len / (size * 2)));
            for (int i = 0; i <= n; i += 1)
            {
                if (i % 2 == 1) continue;
                Vector3 c = Vector3.Lerp(a, b, (float)i / n);
                mb.Box(new Vector3(c.x - size / 2, y, c.z - size / 2), new Vector3(c.x + size / 2, y + height, c.z + size / 2), col);
            }
        }
        Run(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0));
        Run(new Vector3(x0, 0, z1), new Vector3(x1, 0, z1));
        Run(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1));
        Run(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1));
    }

    static void AdobeHouse(MeshBuilder mb, System.Random rng)
    {
        float w = 3.6f + (float)rng.NextDouble(), d = 3.4f + (float)rng.NextDouble(), h = 2.8f + 0.8f * (float)rng.NextDouble();
        float x = w / 2, z = d / 2;
        mb.Box(new Vector3(-x, 0, -z), new Vector3(x, h, z), Adobe);
        mb.Box(new Vector3(-x - 0.12f, h, -z - 0.12f), new Vector3(x + 0.12f, h + 0.18f, z + 0.12f), Trim);
        Crenels(mb, -x, x, -z, z, h + 0.18f, 0.28f, 0.35f, Brick);
        mb.Arch(new Vector3(-x * 0.3f, 0, -z), Vector3.right, Vector3.back, 0.9f, 1.9f, Wood);
        mb.Arch(new Vector3(x * 0.45f, 1.2f, -z), Vector3.right, Vector3.back, 0.55f, 0.9f, Opening);
        mb.Arch(new Vector3(x, 1.2f, 0), Vector3.forward, Vector3.right, 0.55f, 0.9f, Opening);
        mb.Arch(new Vector3(-x, 1.2f, 0), Vector3.back, Vector3.left, 0.55f, 0.9f, Opening);
        for (float vx = -x + 0.3f; vx < x - 0.2f; vx += 0.6f)
            mb.Box(new Vector3(vx, h - 0.45f, -z - 0.3f), new Vector3(vx + 0.12f, h - 0.33f, -z), Wood);
        if (rng.NextDouble() < 0.6)   // a strip of awning
            mb.Quad(new Vector3(-x + 0.2f, h - 0.6f, -z), new Vector3(x - 0.2f, h - 0.6f, -z),
                    new Vector3(x - 0.2f, h - 1.0f, -z - 0.8f), new Vector3(-x + 0.2f, h - 1.0f, -z - 0.8f),
                    rng.NextDouble() < 0.5 ? Cloth : TrimGreen);
    }

    /// <summary>Curved, ringed trunk with a crown of drooping, folded fronds. Returns the crown height.</summary>
    static float Palm(MeshBuilder mb, System.Random rng, float s)
    {
        float H = (5.0f + 2.2f * (float)rng.NextDouble()) * s;
        float leanAngle = 2 * Mathf.PI * (float)rng.NextDouble(), lean = (0.6f + 1.2f * (float)rng.NextDouble()) * s;
        var leanDir = new Vector3(Mathf.Cos(leanAngle), 0, Mathf.Sin(leanAngle));
        var barkA = new Color(0.56f, 0.43f, 0.31f);
        var barkB = new Color(0.47f, 0.36f, 0.26f);
        const int seg = 8;
        Vector3 prev = Vector3.zero;
        float rPrev = 0.27f * s;
        for (int k = 1; k <= seg; k++)
        {
            float t = (float)k / seg;
            Vector3 p = leanDir * (lean * t * t) + Vector3.up * (H * t);
            float r = Mathf.Lerp(0.27f, 0.16f, t) * s;
            mb.Tube(prev, p, rPrev, r, 6, k % 2 == 0 ? barkA : barkB);
            prev = p; rPrev = r;
        }
        Vector3 crown = prev;
        int fronds = 8 + rng.Next(3);
        float g = (float)rng.NextDouble();
        var dark = new Color(0.20f + 0.05f * g, 0.42f + 0.08f * g, 0.16f);
        var light = new Color(0.42f, 0.60f, 0.20f);
        for (int f = 0; f < fronds; f++)
        {
            float a = 2 * Mathf.PI * f / fronds + 0.3f * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = Vector3.Cross(Vector3.up, dir);
            float L = (2.5f + 0.9f * (float)rng.NextDouble()) * s, droop = 0.85f + 0.4f * (float)rng.NextDouble();
            Vector3 Point(float u) => crown + dir * (L * u) + Vector3.up * (L * (0.45f * u - droop * u * u));
            float Width(float u) => 0.44f * s * Mathf.Sin(Mathf.PI * Mathf.Min(u * 1.1f, 1f)) + 0.02f;
            const int n = 6;
            for (int i = 0; i < n; i++)
            {
                float u0 = (float)i / n, u1 = (float)(i + 1) / n;
                Vector3 c0 = Point(u0), c1 = Point(u1);
                float w0 = Width(u0), w1 = Width(u1);
                Vector3 fold0 = c0 + Vector3.up * (0.12f * w0), fold1 = c1 + Vector3.up * (0.12f * w1);   // V-shaped leaf
                Color col = Color.Lerp(dark, light, u0 * 0.8f + 0.2f * (float)rng.NextDouble());
                mb.Quad(fold0, fold1, c1 - side * w1, c0 - side * w0, col);
                mb.Quad(fold0, c0 + side * w0, c1 + side * w1, fold1, col * 0.92f);
            }
        }
        for (int i = 0; i < 3; i++)   // coconuts
        {
            float a = 2 * Mathf.PI * i / 3;
            mb.Icosphere(crown + new Vector3(Mathf.Cos(a) * 0.18f, -0.25f, Mathf.Sin(a) * 0.18f) * s, Vector3.one * 0.13f * s, 0, new Color(0.40f, 0.30f, 0.16f));
        }
        return H;
    }

    static void RuinWall(MeshBuilder mb, System.Random rng)
    {
        int n = 3 + rng.Next(3);
        float x = -0.8f * n / 2;
        for (int i = 0; i < n; i++)
        {
            float w = 0.6f + 0.5f * (float)rng.NextDouble(), h = 0.5f + 1.4f * (float)rng.NextDouble() * (1 - Mathf.Abs(i - n / 2f) / n);
            var col = Color.Lerp(Adobe, Brick, 0.15f * (float)rng.NextDouble());
            mb.Box(new Vector3(x, 0, -0.22f), new Vector3(x + w, h, 0.22f), col);
            x += w;
        }
        // a fallen block
        mb.Box(new Vector3(-0.3f, 0, 0.6f), new Vector3(0.35f, 0.3f, 1.1f), AdobeShade);
    }

    static void TallGrass(MeshBuilder mb, System.Random rng)
    {
        int blades = 6 + rng.Next(5);
        float g = (float)rng.NextDouble();
        var col = new Color(0.58f + 0.15f * g, 0.68f + 0.08f * g, 0.24f + 0.06f * g);
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0, dir.x);
            Vector3 b = dir * 0.08f * (float)rng.NextDouble();
            float h = 0.45f + 0.55f * (float)rng.NextDouble(), bend = 0.15f + 0.25f * (float)rng.NextDouble();
            Vector3 mid = b + dir * (bend * 0.35f) + Vector3.up * (h * 0.55f);
            Vector3 tip = b + dir * bend + Vector3.up * h;
            mb.Quad(b - side * 0.04f, b + side * 0.04f, mid + side * 0.03f, mid - side * 0.03f, col * 0.85f);
            mb.Tri(mid - side * 0.03f, mid + side * 0.03f, tip, col);
        }
    }

    static void DryTuft(MeshBuilder mb, System.Random rng)
    {
        int blades = 4 + rng.Next(4);
        float g = (float)rng.NextDouble();
        var col = new Color(0.74f + 0.1f * g, 0.64f + 0.08f * g, 0.38f);
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0, dir.x);
            float h = 0.2f + 0.25f * (float)rng.NextDouble();
            Vector3 tip = dir * (0.12f + 0.12f * (float)rng.NextDouble()) + Vector3.up * h;
            mb.Tri(-side * 0.03f, side * 0.03f, tip, col);
        }
    }

    // ------------------------------------------------------------------ golf props (local meshes)

    public static Mesh FlagMesh()
    {
        var mb = new MeshBuilder(0.6f);
        mb.Box(new Vector3(-0.025f, 0, -0.025f), new Vector3(0.025f, 2.4f, 0.025f), new Color(0.95f, 0.95f, 0.92f));
        mb.Tri(new Vector3(0, 2.38f, 0), new Vector3(0, 1.82f, 0), new Vector3(0.8f, 2.1f, 0.0f), new Color(0.95f, 0.15f, 0.12f));
        mb.Icosphere(new Vector3(0, 2.43f, 0), Vector3.one * 0.05f, 0, new Color(0.95f, 0.8f, 0.2f));
        return mb.ToMesh("S3 Flag");
    }

    public static Mesh BallMesh(float r)
    {
        var mb = new MeshBuilder(1f);
        var red = new Color(0.9f, 0.15f, 0.1f);
        mb.Icosphere(Vector3.zero, Vector3.one * r, 2, Color.white, null, 0,
                     c => Mathf.Abs(c.y) < 0.22f || (c.x > 0.75f) ? red : new Color(0.97f, 0.97f, 0.95f));
        return mb.ToMesh("S3 Ball");
    }

    /// <summary>Launch guide: dots along local +z from just past the ball, yellow to orange.</summary>
    public static Mesh GuideMesh(float ballRadius)
    {
        var mb = new MeshBuilder(1f);
        const int n = 9;
        for (int k = 0; k < n; k++)
        {
            var col = Color.Lerp(new Color(1f, 0.95f, 0.3f), new Color(1f, 0.42f, 0.08f), k / (n - 1f));
            mb.Icosphere(new Vector3(0, 0, ballRadius + 0.2f + 0.32f * k), Vector3.one * (0.06f - 0.003f * k), 1, col);
        }
        return mb.ToMesh("S3 Guide");
    }

    /// <summary>Flat aiming arrow lying on the ground along +z, starting just past the ball.</summary>
    public static Mesh ArrowMesh(float ballRadius)
    {
        var mb = new MeshBuilder(0.3f);
        var col = new Color(1f, 0.95f, 0.55f);
        float y = 0.015f, z0 = ballRadius + 0.2f, z1 = z0 + 1.6f;
        mb.Quad(new Vector3(-0.06f, y, z0), new Vector3(0.06f, y, z0), new Vector3(0.06f, y, z1), new Vector3(-0.06f, y, z1), col);
        mb.Tri(new Vector3(-0.2f, y, z1), new Vector3(0.2f, y, z1), new Vector3(0, y, z1 + 0.4f), col);
        return mb.ToMesh("S3 Arrow");
    }
}
