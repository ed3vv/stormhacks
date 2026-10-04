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
        world.biome == S3World.Biome.Desert ? BuildDesert(world, rng) :
        world.biome == S3World.Biome.Basin ? BuildBasin(world, rng) : BuildMeadow(world, rng);

    /// <summary>
    /// Keep tees and greens clear of props. The line between them is deliberately left alone:
    /// trees and buildings in the way are part of each hole. Laser puzzles are different: the
    /// solved light (including beams that go right round the world) and the pieces must stay clear,
    /// or a tree could make a puzzle impossible.
    /// </summary>
    static List<KeepOut> CourseKeepOut(S3World world)
    {
        var keep = new List<KeepOut>();
        foreach (var h in world.holes)
        {
            keep.Add(new KeepOut(h.tee, 3f));
            keep.Add(new KeepOut(h.cup, world.greenRadius + 1f));
        }
        foreach (var level in world.levels)
        {
            foreach (var p in S3Levels.SolutionFootprint(level, world.radius)) keep.Add(new KeepOut(p, 1.1f));
            foreach (var b in level.solution) keep.Add(new KeepOut(b.position, 2f));   // room to stand and place
            if (level.diffractors > 0)   // the diffracted light comes down round the crystals
                foreach (var b in level.blocks)
                    if (b.kind == S3Laser.Kind.Goal) keep.Add(new KeepOut(b.position, 3f));
        }
        if (world.biome == S3World.Biome.Desert)   // an open arena for the puzzles
            keep.Add(new KeepOut(world.LocalFrame(world.puzzleFrame.x, world.puzzleFrame.y, world.puzzleFrame.z) *
                                 S3.GroundPoint(0.5f, 4.5f, world.radius), 8.5f));
        return keep;
    }

    /// <summary>
    /// A landmark at its preferred spot, or the nearest clear spot to it (spiralling outwards: 600 tries reach ~18 m,
    /// 2400 the whole world), so fixed buildings never land on a puzzle's light. Returns false if nothing is clear.
    /// </summary>
    static bool FixedSpot(S3World world, List<KeepOut> keep, float x, float z, float yawDeg, float radius, out Matrix4x4 frame, int tries = 600)
    {
        float R = world.radius;
        for (int k = 0; k < tries; k++)
        {
            float d = 0.75f * Mathf.Sqrt(k), a = k * 2.39996f;   // golden-angle spiral
            frame = S3.GroundFrame(x + d * Mathf.Cos(a), z + d * Mathf.Sin(a), yawDeg * Mathf.Deg2Rad, R);
            if (Clear(frame.GetColumn(3), R, keep, radius)) return true;
        }
        frame = Matrix4x4.identity;
        return false;
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
        if (FixedSpot(world, keep, -10, 13, -35, 7f, out var fortFrame))
        {
            var frame = fortFrame;
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
            if (!FixedSpot(world, keep, hd.x, hd.y, hd.z, 4.5f, out var frame)) continue;
            var mb = new MeshBuilder();
            AdobeHouse(mb, rng);
            Add(world, scene, mb, frame, 3.2f, 5.5f, 0, 0.25f, 2.8f, 4.5f, 3f, 4f);
            keep.Add(new KeepOut(frame.GetColumn(3), 4.5f));
        }

        // grass oases: under the puzzle arena, around every green, and a few scattered ones
        {
            var arena = world.LocalFrame(world.puzzleFrame.x, world.puzzleFrame.y, world.puzzleFrame.z) * S3.GroundFrame(0.5f, 4.5f, 0, R);
            world.RegisterPatch(arena.GetColumn(3), 7.5f);
            oases.Add((arena, 10f));
        }
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

    // ================================================================== eternal poles

    static readonly Color Snow = new Color(0.94f, 0.96f, 0.99f);
    static readonly Color SnowShade = new Color(0.79f, 0.85f, 0.93f);
    static readonly Color PineGreen = new Color(0.23f, 0.43f, 0.40f);
    static readonly Color IceLight = new Color(0.79f, 0.93f, 0.97f);
    static readonly Color IceMid = new Color(0.62f, 0.84f, 0.93f);
    static readonly Color IceRoof = new Color(0.44f, 0.71f, 0.89f);
    static readonly Color IceDoor = new Color(0.25f, 0.44f, 0.60f);

    static MeshBuilder BuildBasin(S3World world, System.Random rng)
    {
        float R = world.radius;
        var scene = new MeshBuilder();
        var keep = CourseKeepOut(world);
        var lake = world.basinLake;
        world.RegisterPatch(S3.GroundPoint(lake.x, lake.y, R), lake.z);   // the frozen lake (cosmetic)
        // small decoration doesn't stop anything, so it may lie in the light too: it only keeps off tees, greens,
        // the lake, the locks' own blocks and pieces, and whatever has been put down already
        var decor = new List<KeepOut>();
        foreach (var h in world.holes) { decor.Add(new KeepOut(h.tee, 3f)); decor.Add(new KeepOut(h.cup, world.greenRadius + 1f)); }
        decor.Add(new KeepOut(S3.GroundPoint(lake.x, lake.y, R), lake.z));
        foreach (var level in world.levels)
        {
            foreach (var b in level.blocks) decor.Add(new KeepOut(b.position, 0.6f + b.size.x));
            foreach (var b in level.solution) decor.Add(new KeepOut(b.position, 1f));
        }
        int fenceKeep = keep.Count;
        BuildFences(world, keep);
        for (int i = fenceKeep; i < keep.Count; i++) decor.Add(keep[i]);
        // new props that stop the ball stay off the straight line from each tee to its cup (the fences are the obstacles there)
        var lines = new List<KeepOut>();
        foreach (var h in world.holes)
        {
            Vector4 d = S3.DirTo(h.tee, h.cup);
            float len = S3.Dist(h.tee, h.cup);
            if (d == Vector4.zero || len > 0.97f * Mathf.PI) continue;   // the antipode hole has no one line
            for (float t = 0; t <= len; t += 1f / R) lines.Add(new KeepOut(S3.Walk(h.tee, d, t), 1.5f));
        }

        // landmarks, after the concept: ice castle across the lake, an igloo by the shore, a snowman by the first tee
        if (FixedSpot(world, keep, -12.5f, 22f, 35, 4.6f, out var f, 2400))
        {
            var mb = new MeshBuilder();
            IceCastle(mb);
            Add(world, scene, mb, f, 4.6f, 9f, 0, 0.2f, 3.8f, 8f, 4.2f, 6f);
            keep.Add(new KeepOut(f.GetColumn(3), 5.5f));
            decor.Add(new KeepOut(f.GetColumn(3), 4.5f));
        }
        if (FixedSpot(world, keep, 7.5f, 14f, -160, 2.6f, out f))
        {
            var mb = new MeshBuilder();
            Igloo(mb);
            Add(world, scene, mb, f, 2.2f, 2.2f, 0, 0.25f, 1.8f, 1.8f, 2f, 1.8f);
            keep.Add(new KeepOut(f.GetColumn(3), 2.8f));
            decor.Add(new KeepOut(f.GetColumn(3), 2.6f));
        }
        if (world.holes.Count > 0)
        {
            Vector2 t = world.basinCourse[0].tee;
            if (FixedSpot(world, keep, t.x + 2.8f, t.y + 1.5f, 200, 0.8f, out f))
            {
                var mb = new MeshBuilder(0.4f);
                Snowman(mb);
                Add(world, scene, mb, f, 0.8f, 2.3f, 0, 0.15f, 0.55f, 2f, 0.8f, 2.2f);
                keep.Add(new KeepOut(f.GetColumn(3), 1f));
                decor.Add(new KeepOut(f.GetColumn(3), 1f));
            }
        }

        // snowy pines, sparse (and kept off the locks' light) so the puzzles read clearly
        for (int i = 0; i < 24; i++)
        {
            if (!TryPlace(rng, R, keep, 1.8f, out var frame)) continue;
            var mb = new MeshBuilder();
            float s = 0.75f + 0.35f * (float)rng.NextDouble();
            SnowyPine(mb, rng, s);
            Add(world, scene, mb, frame, 1.4f * s, 4.8f * s, 0.15f, 0.35f, 0.3f * s, 6f, 1.3f * s, 4f * s);
            keep.Add(new KeepOut(frame.GetColumn(3), 1.6f * s));
            decor.Add(new KeepOut(frame.GetColumn(3), 0.8f));
        }

        // crystal spires
        for (int i = 0; i < 8; i++)
        {
            if (!TryPlace(rng, R, keep, 1.2f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f);
            float h = Crystals(mb, rng);
            Add(world, scene, mb, frame, 1.1f, h, 0, 0.2f, 0.6f, h, 1f, h, 0.6f);
            keep.Add(new KeepOut(frame.GetColumn(3), 1.3f));
            decor.Add(new KeepOut(frame.GetColumn(3), 1.1f));
        }

        // snowdrifts
        for (int i = 0; i < 9; i++)
        {
            float rx = 2.2f + 1.5f * (float)rng.NextDouble(), rz = 1.2f + 0.8f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, keep, rx + 0.8f, out var frame)) continue;
            var mb = new MeshBuilder(1.2f);
            mb.Icosphere(new Vector3(0, -0.25f, 0), new Vector3(rx, 0.75f, rz), 2, Snow, rng, 0.05f);
            Add(world, scene, mb, frame, rx, 0.6f, 0, 0, Mathf.Min(rx, rz) * 0.7f, 0.5f, rx * 0.8f, 0.5f, 0.4f);
            keep.Add(new KeepOut(frame.GetColumn(3), rx));
            decor.Add(new KeepOut(frame.GetColumn(3), rx));
        }

        // dark rocks with snow caps
        for (int i = 0; i < 11; i++)
        {
            float size = 0.45f + 1.1f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame, attempts: 120)) continue;
            var mb = new MeshBuilder(0.6f);
            float g = 0.30f + 0.06f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.3f, 0), new Vector3(size * 1.1f, size * 0.75f, size), 1,
                         new Color(g, g * 1.05f, g * 1.2f), rng, 0.2f,
                         c => c.y > 0.45f ? Snow : new Color(g, g * 1.05f, g * 1.2f));
            Add(world, scene, mb, frame, size * 1.1f, size, 0, 0.3f, size * 0.95f, size, size, size, 0.8f);
            keep.Add(new KeepOut(frame.GetColumn(3), size + 0.4f));
            decor.Add(new KeepOut(frame.GetColumn(3), size + 0.2f));
        }

        // rock instead of trees for the rest: tors, cairns and arches of ice that stop the ball, kept off the
        // obvious lines and the locks' light. Scattered all round the world, they also give the far side, which
        // hangs overhead as the sky, something to show
        var solid = new List<KeepOut>(keep);
        solid.AddRange(lines);
        void KeepClear(Vector4 p, float r) { keep.Add(new KeepOut(p, r)); solid.Add(new KeepOut(p, r)); decor.Add(new KeepOut(p, r)); }
        for (int i = 0; i < 8; i++)
        {
            var mb = new MeshBuilder(0.6f);
            float reach = Tor(mb, rng);
            if (!TryPlace(rng, R, solid, reach, out var frame, attempts: 300)) continue;
            Add(world, scene, mb, frame, reach, 1.6f, 0, 0.3f, reach * 0.8f, 1.4f, reach, 1.4f, 0.8f);
            KeepClear(frame.GetColumn(3), reach + 0.5f);
        }
        for (int i = 0; i < 5; i++)
        {
            if (!TryPlace(rng, R, solid, 0.9f, out var frame, attempts: 200)) continue;
            var mb = new MeshBuilder(0.4f);
            float h = Cairn(mb, rng);
            Add(world, scene, mb, frame, 0.6f, h + 0.3f, 0, 0.25f, 0.4f, h, 0.6f, h, 0.6f);
            KeepClear(frame.GetColumn(3), 1f);
        }
        for (int i = 0, tries = 0; i < 2 && tries < 400; tries++)
        {
            // only the legs need clear ground: the ball, the walker and the light all pass under the span
            var mb = new MeshBuilder(0.5f);
            float half = IceArch(mb, rng, out float rise);
            if (!TryPlace(rng, R, keep, 0.5f, out var frame, attempts: 1)) continue;
            Vector4 left = (frame * S3.Exp(new Vector3(-half, 0, 0), R)).normalized, right = (frame * S3.Exp(new Vector3(half, 0, 0), R)).normalized;
            if (!Clear(left, R, solid, 0.6f) || !Clear(right, R, solid, 0.6f)) continue;
            Add(world, scene, mb, frame, half + 0.6f, rise + 0.5f, 0, 0.15f, shadowRadius: half, shadowHeight: rise, shadowStrength: 0.5f);
            foreach (var leg in new[] { left, right })
            {
                world.obstacles.Add(new S3World.Obstacle(leg, 0.45f, rise));
                KeepClear(leg, 1.1f);
            }
            i++;
        }

        // frozen ponds (cosmetic, like the lake)
        var open = new List<KeepOut>();
        foreach (var h in world.holes) { open.Add(new KeepOut(h.tee, 3f)); open.Add(new KeepOut(h.cup, world.greenRadius + 2f)); }
        open.Add(new KeepOut(S3.GroundPoint(lake.x, lake.y, R), lake.z + 2f));
        for (int i = 0; i < 4; i++)
        {
            float r = 2f + 1.6f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, open, r + 1f, out var frame)) continue;
            world.RegisterPatch(frame.GetColumn(3), r);
            open.Add(new KeepOut(frame.GetColumn(3), r + 1f));
            keep.Add(new KeepOut(frame.GetColumn(3), r + 0.3f));
            decor.Add(new KeepOut(frame.GetColumn(3), r));
        }

        // small things that don't stop anything: pebbles, shards of ice, little mounds of snow, stones under the snow,
        // and frosted reeds
        for (int i = 0; i < 60; i++)
        {
            if (!TryPlace(rng, R, decor, 0.3f, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            Pebbles(mb, rng);
            Add(world, scene, mb, frame, occlusion: 0.2f);
            decor.Add(new KeepOut(frame.GetColumn(3), 0.5f));
        }
        for (int i = 0; i < 30; i++)
        {
            if (!TryPlace(rng, R, decor, 0.4f, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            IceShards(mb, rng);
            Add(world, scene, mb, frame, occlusion: 0.15f);
            decor.Add(new KeepOut(frame.GetColumn(3), 0.5f));
        }
        for (int i = 0; i < 24; i++)
        {
            float rx = 0.6f + 0.7f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, decor, rx, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            mb.Icosphere(new Vector3(0, -0.12f, 0), new Vector3(rx, 0.32f, rx * (0.6f + 0.3f * (float)rng.NextDouble())), 1,
                         Color.Lerp(Snow, SnowShade, 0.25f * (float)rng.NextDouble()), rng, 0.06f);
            Add(world, scene, mb, frame, occlusion: 0.15f, shadowRadius: rx * 0.8f, shadowHeight: 0.3f, shadowStrength: 0.25f);
            decor.Add(new KeepOut(frame.GetColumn(3), rx));
        }
        for (int i = 0; i < 40; i++)
        {
            if (!TryPlace(rng, R, decor, 0.5f, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            float s = 0.25f + 0.35f * (float)rng.NextDouble(), g = 0.32f + 0.08f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, -0.02f, 0), new Vector3(s, 0.07f, s * 0.8f), 1, new Color(g, g * 1.04f, g * 1.16f), rng, 0.2f,
                         c => c.y > 0.6f ? Color.Lerp(Snow, SnowShade, 0.3f) : new Color(g, g * 1.04f, g * 1.16f));
            Add(world, scene, mb, frame, occlusion: 0f);
        }
        for (int i = 0; i < 140; i++)
        {
            if (!TryPlace(rng, R, decor, 0.2f, out var frame, attempts: 3)) continue;
            var mb = new MeshBuilder(1f);
            FrostTuft(mb, rng);
            Add(world, scene, mb, frame, sway: 0.6f, occlusion: 0.25f);
        }
        return scene;
    }

    // ------------------------------------------------------------------ eternal poles: fences and rock

    /// <summary>
    /// The golf fences (S3World.basinFences): post-and-rail panels in an arc round the tee side of a green, left out
    /// where that hole's lock stands. Each hole's panels are boxes in world.holeFences (they stop the ball and the
    /// walker while the hole is on; light slips between the rails) and get a renderer of their own, so S3World.SetActiveHole
    /// can put them up and take them down. Other props keep clear of every fence, and of the open side's approach:
    /// the way in from the tee's antipode, where a shot straight over the pole comes down.
    /// </summary>
    static void BuildFences(S3World world, List<KeepOut> keep)
    {
        float R = world.radius;
        foreach (var def in world.basinFences)
        {
            if (def.hole < 0 || def.hole >= world.holes.Count) continue;
            var hole = world.holes[def.hole];
            var own = def.hole < world.levels.Count ? world.levels[def.hole] : null;
            Vector4 toTee = S3.DirTo(hole.cup, hole.tee);
            if (toTee == Vector4.zero) toTee = S3.Ortho(S3.EZ, hole.cup, S3.EY);
            float span = 2 * def.halfSpan * Mathf.Deg2Rad;
            int n = Mathf.Max(2, Mathf.CeilToInt(span * def.radius / 1.4f));
            var pts = new Vector4[n + 1];
            for (int k = 0; k <= n; k++)
                pts[k] = S3.Walk(hole.cup, S3Laser.Turn(toTee, hole.cup, -def.halfSpan * Mathf.Deg2Rad + span * k / n), def.radius / R);
            var panels = new S3Laser.Block[n];
            for (int k = 0; k < n; k++)
            {
                var panel = S3Laser.Block.Wall(pts[k], pts[k + 1], R, 0.16f, 1.2f);
                bool clear = own == null || (!own.blocks.Exists(b => panel.FootprintOverlaps(b, 0.25f / R))
                                             && !own.solution.Exists(b => panel.FootprintContains(b.position, 1.4f / R)));
                if (clear) panels[k] = panel;
            }
            var list = new List<S3Laser.Block>();
            var mesh = new MeshBuilder();
            for (int k = 0; k < n; k++)
            {
                var panel = panels[k];
                if (panel == null) continue;
                list.Add(panel);
                var mb = new MeshBuilder(0.4f);
                float hl = S3.Dist(pts[k], pts[k + 1]) * R / 2;
                FencePanel(mb, hl, true, k == n - 1 || panels[k + 1] == null);
                mesh.AppendBaked(mb, S3.FrameAt(panel.position, S3.EY, S3.DirTo(panel.position, pts[k + 1])), R, 0, 0, 0.3f);
                keep.Add(new KeepOut(panel.position, hl + 0.8f));
            }
            world.holeFences[def.hole] = list;
            world.fenceRenderers[def.hole] = world.CreateRenderer($"Fence {def.hole + 1}", mesh.ToMesh($"S3 Fence {def.hole + 1}"), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
            // keep the approach from the open side clear, out to past where a shot over the pole from the tee lands
            Vector4 back = -toTee;
            float land = Mathf.PI - S3.Dist(hole.tee, hole.cup);
            for (float t = (def.radius + 0.5f) / R; t <= land + 2f / R; t += 1f / R)
                keep.Add(new KeepOut(S3.Walk(hole.cup, back, t), 1.8f));
        }
    }

    /// <summary>One fence panel along local z (-hl..hl): posts capped with snow, two rails clear of the light (0.45 m up), a drift at its foot.</summary>
    static void FencePanel(MeshBuilder mb, float hl, bool startPost, bool endPost)
    {
        var post = new Color(0.36f, 0.26f, 0.19f);
        var rail = new Color(0.48f, 0.35f, 0.25f);
        void Post(float z)
        {
            mb.Box(new Vector3(-0.07f, 0, z - 0.07f), new Vector3(0.07f, 1.22f, z + 0.07f), post);
            mb.Frustum(new Vector3(0, 1.22f, z), 0.11f, 0.03f, 0.13f, 6, Snow);
        }
        if (startPost) Post(-hl);
        if (endPost) Post(hl);
        foreach (float y in new[] { 0.18f, 0.78f })
        {
            mb.Box(new Vector3(-0.03f, y, -hl), new Vector3(0.03f, y + 0.12f, hl), rail);
            mb.Box(new Vector3(-0.045f, y + 0.12f, -hl), new Vector3(0.045f, y + 0.16f, hl), Snow);
        }
        mb.Icosphere(new Vector3(0, -0.06f, 0), new Vector3(0.26f, 0.2f, hl * 0.95f), 1, Color.Lerp(Snow, SnowShade, 0.2f));
    }

    /// <summary>A tor: a few snow-capped boulders heaped together. Returns how far it reaches from its centre.</summary>
    static float Tor(MeshBuilder mb, System.Random rng)
    {
        int n = 3 + rng.Next(3);
        float reach = 0;
        for (int i = 0; i < n; i++)
        {
            float s = (i == 0 ? 0.9f : 0.4f) + 0.45f * (float)rng.NextDouble();
            float a = 2 * Mathf.PI * (float)rng.NextDouble(), d = i == 0 ? 0 : 0.55f + 0.55f * (float)rng.NextDouble();
            float g = 0.27f + 0.08f * (float)rng.NextDouble();
            var rock = new Color(g, g * 1.04f, g * 1.18f);
            mb.Icosphere(new Vector3(Mathf.Cos(a) * d, s * 0.35f, Mathf.Sin(a) * d), new Vector3(s * 1.1f, s * 0.8f, s), 1, rock, rng, 0.22f,
                         c => c.y > 0.4f ? Snow : rock);
            reach = Mathf.Max(reach, d + s);
        }
        return reach;
    }

    /// <summary>A cairn of flat stones with snow on top. Returns its height.</summary>
    static float Cairn(MeshBuilder mb, System.Random rng)
    {
        int n = 4 + rng.Next(3);
        float y = 0;
        for (int i = 0; i < n; i++)
        {
            float w = Mathf.Lerp(0.42f, 0.16f, (float)i / n) * (0.85f + 0.3f * (float)rng.NextDouble());
            float t = 0.14f + 0.08f * (float)rng.NextDouble(), g = 0.36f + 0.12f * (float)rng.NextDouble();
            var stone = new Color(g, g * 1.02f, g * 1.1f);
            var c = new Vector3(0.05f * ((float)rng.NextDouble() - 0.5f), y + t * 0.5f, 0.05f * ((float)rng.NextDouble() - 0.5f));
            mb.Icosphere(c, new Vector3(w, t * 0.55f, w * 0.85f), 1, stone, rng, 0.12f, f => f.y > 0.7f ? Color.Lerp(stone, Snow, 0.7f) : stone);
            y += t * 0.92f;
        }
        mb.Icosphere(new Vector3(0, y + 0.02f, 0), new Vector3(0.12f, 0.07f, 0.12f), 0, Snow);
        return y;
    }

    /// <summary>A natural arch of blue ice with icicles under its span. Returns the half-width between its legs.</summary>
    static float IceArch(MeshBuilder mb, System.Random rng, out float rise)
    {
        float half = 1.5f + 0.6f * (float)rng.NextDouble(), top = 2.1f + 0.8f * (float)rng.NextDouble();
        rise = top;
        var ice = new Color(IceMid.r, IceMid.g, IceMid.b, 0.8f);
        var deep = new Color(IceRoof.r, IceRoof.g, IceRoof.b, 0.75f);
        const int seg = 12;
        float Radius(float u) => Mathf.Lerp(0.5f, 0.26f, Mathf.Sin(Mathf.PI * u));
        Vector3 At(float u) => new Vector3(-half * Mathf.Cos(Mathf.PI * u), top * Mathf.Sin(Mathf.PI * u), 0.12f * Mathf.Sin(2 * Mathf.PI * u));
        for (int k = 0; k < seg; k++)
        {
            float u0 = (float)k / seg, u1 = (float)(k + 1) / seg;
            mb.Tube(At(u0) - (k == 0 ? Vector3.up * 0.2f : Vector3.zero), At(u1), Radius(u0), Radius(u1), 7, k % 3 == 1 ? deep : ice);
        }
        mb.Frustum(new Vector3(-half, 0, 0), 0.75f, 0.45f, 0.35f, 7, Snow);   // snow banked round the legs
        mb.Frustum(new Vector3(half, 0, 0), 0.75f, 0.45f, 0.35f, 7, Snow);
        for (int i = 1; i < 8; i++)
        {
            Vector3 p = At(0.2f + 0.6f * i / 8f) - Vector3.up * Radius(0.5f) * 0.8f;
            float len = 0.15f + 0.25f * (float)rng.NextDouble();
            mb.Tube(p, p - Vector3.up * len, 0.05f, 0.005f, 5, ice);
        }
        return half;
    }

    /// <summary>A few small stones, dusted with snow.</summary>
    static void Pebbles(MeshBuilder mb, System.Random rng)
    {
        int n = 2 + rng.Next(4);
        for (int i = 0; i < n; i++)
        {
            float s = 0.05f + 0.1f * (float)rng.NextDouble(), g = 0.3f + 0.15f * (float)rng.NextDouble();
            var stone = new Color(g, g * 1.03f, g * 1.12f);
            var c = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, s * 0.25f, ((float)rng.NextDouble() - 0.5f) * 0.9f);
            mb.Icosphere(c, new Vector3(s, s * 0.6f, s * 0.8f), 0, stone, rng, 0.2f, f => f.y > 0.55f ? Color.Lerp(stone, Snow, 0.6f) : stone);
        }
    }

    /// <summary>Two to four blades of ice sticking out of the snow.</summary>
    static void IceShards(MeshBuilder mb, System.Random rng)
    {
        int n = 2 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble(), lean = 0.15f + 0.35f * (float)rng.NextDouble(), h = 0.2f + 0.4f * (float)rng.NextDouble();
            var b = new Vector3(Mathf.Cos(a) * 0.12f, -0.05f, Mathf.Sin(a) * 0.12f);
            var tip = b + new Vector3(Mathf.Cos(a) * lean * h, h, Mathf.Sin(a) * lean * h);
            mb.Tube(b, tip, 0.06f + 0.03f * (float)rng.NextDouble(), 0.004f, 5, i % 2 == 0 ? new Color(IceLight.r, IceLight.g, IceLight.b, 0.7f) : new Color(IceMid.r, IceMid.g, IceMid.b, 0.7f));
        }
    }

    static void SnowyPine(MeshBuilder mb, System.Random rng, float s)
    {
        mb.Frustum(Vector3.zero, 0.2f * s, 0.14f * s, 1.1f * s, 6, new Color(0.35f, 0.25f, 0.19f));
        float g = (float)rng.NextDouble() * 0.05f;
        for (int k = 0; k < 3; k++)
        {
            // slimmer tiers under thicker snow, so from above they read as frosted trees rather than dark blots
            float y = (0.8f + 0.95f * k) * s, r = (1.3f - 0.34f * k) * s, h = 1.7f * s;
            mb.Frustum(new Vector3(0, y, 0), r, 0, h, 7, new Color(PineGreen.r + g, PineGreen.g + g, PineGreen.b + g), k * 0.4f);
            mb.Frustum(new Vector3(0, y + h * 0.36f, 0), r * 0.68f, 0, h * 0.64f + 0.02f, 7, Snow, k * 0.4f);
        }
    }

    /// <summary>A cluster of tilted hexagonal ice prisms. Returns its height.</summary>
    static float Crystals(MeshBuilder mb, System.Random rng)
    {
        var cols = new[] { new Color(0.62f, 0.89f, 0.96f, 0.75f), new Color(0.43f, 0.76f, 0.90f, 0.75f), new Color(0.77f, 0.95f, 0.98f, 0.75f) };
        int n = 3 + rng.Next(3);
        float top = 0;
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble(), tilt = 0.1f + 0.35f * (float)rng.NextDouble(), h = 1.2f + 2.2f * (float)rng.NextDouble();
            var basePt = new Vector3(Mathf.Cos(a) * 0.3f, 0, Mathf.Sin(a) * 0.3f);
            var tipBase = basePt + new Vector3(Mathf.Cos(a) * tilt * h, h, Mathf.Sin(a) * tilt * h);
            var tip = tipBase + (tipBase - basePt).normalized * 0.5f;
            var c = cols[i % cols.Length];
            mb.Tube(basePt, tipBase, 0.24f, 0.22f, 6, c);
            mb.Tube(tipBase, tip, 0.22f, 0.01f, 6, c);
            top = Mathf.Max(top, tip.y);
        }
        return top;
    }

    static void Igloo(MeshBuilder mb)
    {
        const float r = 1.7f;
        for (int k = 0; k < 5; k++)
        {
            float a0 = Mathf.PI / 2 * k / 5, a1 = Mathf.PI / 2 * (k + 1) / 5;
            mb.Frustum(new Vector3(0, r * Mathf.Sin(a0), 0), r * Mathf.Cos(a0), Mathf.Max(r * Mathf.Cos(a1), 0.01f),
                       r * (Mathf.Sin(a1) - Mathf.Sin(a0)), 12, k % 2 == 1 ? new Color(0.89f, 0.93f, 0.97f) : new Color(0.85f, 0.89f, 0.94f));
        }
        mb.Box(new Vector3(-0.5f, 0, -2.5f), new Vector3(0.5f, 0.9f, -1.2f), new Color(0.89f, 0.93f, 0.97f));
        mb.Quad(new Vector3(-0.32f, 0, -2.52f), new Vector3(0.32f, 0, -2.52f), new Vector3(0.32f, 0.7f, -2.52f), new Vector3(-0.32f, 0.7f, -2.52f), new Color(0.23f, 0.31f, 0.43f));
    }

    static void IceCastle(MeshBuilder mb)
    {
        void Tower(float x, float z, float r, float h)
        {
            mb.Frustum(new Vector3(x, 0, z), r, r * 0.9f, h, 6, IceLight, Mathf.PI / 6);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI / 6 + 2 * Mathf.PI * i / 6;
                float cx = x + Mathf.Cos(a) * r * 0.85f, cz = z + Mathf.Sin(a) * r * 0.85f;
                mb.Box(new Vector3(cx - 0.18f, h, cz - 0.18f), new Vector3(cx + 0.18f, h + 0.4f, cz + 0.18f), IceMid);
            }
            mb.Frustum(new Vector3(x, h + 0.1f, z), r * 0.75f, 0, r * 1.4f, 6, IceRoof, Mathf.PI / 6);
        }
        Tower(0, 0, 1.6f, 7); Tower(-3.4f, 0.4f, 1f, 4.6f); Tower(3.2f, 0.2f, 1f, 5.2f);
        mb.Box(new Vector3(-3, 0, -0.5f), new Vector3(3, 2.8f, 0.6f), IceMid);
        for (float x = -2.8f; x < 2.8f; x += 0.7f) mb.Box(new Vector3(x, 2.8f, -0.5f), new Vector3(x + 0.35f, 3.2f, 0.6f), IceLight);
        mb.Quad(new Vector3(-0.45f, 0, -0.52f), new Vector3(0.45f, 0, -0.52f), new Vector3(0.45f, 1.9f, -0.52f), new Vector3(-0.45f, 1.9f, -0.52f), IceDoor);
        mb.Quad(new Vector3(-0.25f, 4.5f, -1.62f), new Vector3(0.25f, 4.5f, -1.62f), new Vector3(0.25f, 5.3f, -1.62f), new Vector3(-0.25f, 5.3f, -1.62f), IceDoor);
    }

    static void Snowman(MeshBuilder mb)
    {
        var w = new Color(0.96f, 0.97f, 0.99f);
        var black = new Color(0.11f, 0.11f, 0.13f);
        mb.Icosphere(new Vector3(0, 0.55f, 0), new Vector3(0.6f, 0.55f, 0.6f), 1, w);
        mb.Icosphere(new Vector3(0, 1.38f, 0), new Vector3(0.44f, 0.42f, 0.44f), 1, w);
        mb.Icosphere(new Vector3(0, 2.02f, 0), new Vector3(0.32f, 0.32f, 0.32f), 1, w);
        mb.Frustum(new Vector3(0, 1.7f, 0), 0.36f, 0.36f, 0.14f, 10, new Color(0.90f, 0.31f, 0.24f));
        mb.Tube(new Vector3(0, 2.04f, -0.28f), new Vector3(0, 2.0f, -0.68f), 0.07f, 0.01f, 6, new Color(0.94f, 0.54f, 0.17f));
        foreach (float x in new[] { -0.11f, 0.11f }) mb.Icosphere(new Vector3(x, 2.14f, -0.28f), Vector3.one * 0.04f, 0, black);
        foreach (float y in new[] { 1.25f, 1.45f }) mb.Icosphere(new Vector3(0, y, -0.43f), Vector3.one * 0.04f, 0, black);
    }

    static void FrostTuft(MeshBuilder mb, System.Random rng)
    {
        int blades = 3 + rng.Next(3);
        float g = (float)rng.NextDouble();
        var col = new Color(0.70f + 0.08f * g, 0.74f + 0.06f * g, 0.68f);
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0, dir.x);
            float h = 0.25f + 0.3f * (float)rng.NextDouble();
            Vector3 tip = dir * (0.06f + 0.1f * (float)rng.NextDouble()) + Vector3.up * h;
            mb.Tri(-side * 0.025f, side * 0.025f, tip, col);
        }
    }

    /// <summary>The ice barrier over a sealed cup: a glowing hemisphere with brighter ribs, in local metres.</summary>
    public static Mesh DomeMesh(float r)
    {
        var mb = new MeshBuilder(0.6f);
        const int lat = 7, lon = 28;
        Vector3 P(int i, int j)
        {
            float a = Mathf.PI / 2 * i / lat, b = 2 * Mathf.PI * j / lon;
            return new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b)) * r;
        }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                float k = 1 - (float)i / lat;
                var col = new Color(0.55f, 0.85f, 1f, 0.10f + 0.16f * k * k + (j % 4 == 0 ? 0.12f : 0));
                mb.Quad(P(i, j), P(i, j + 1), P(i + 1, j + 1), P(i + 1, j), col);
            }
        return mb.ToMesh("S3 Barrier");
    }

    // ================================================================== laser puzzle pieces

    /// <summary>
    /// The mesh for a puzzle block in its own frame: local x = side, y = up, z = facing (the axis a
    /// mirror reflects about). Vertex alpha below 1 glows (lenses, crystals).
    /// </summary>
    public static Mesh BlockMesh(S3Laser.Block b, S3World.Biome biome)
    {
        var mb = new MeshBuilder(0.4f);
        float hl = b.size.x, hw = b.size.y, h = b.size.z;
        var metal = new Color(0.24f, 0.26f, 0.32f);
        switch (b.kind)
        {
            case S3Laser.Kind.Wall:
                if (b.gate)
                {
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h, hl), new Color(0.70f, 0.90f, 1f, 0.8f));
                    mb.Box(new Vector3(-hw * 0.6f, h, -hl * 0.8f), new Vector3(hw * 0.6f, h + 0.18f, hl * 0.8f), new Color(0.85f, 0.97f, 1f, 0.7f));
                }
                else if (biome == S3World.Biome.Desert)
                {
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h, hl), Adobe);
                    mb.Box(new Vector3(-hw - 0.04f, h, -hl - 0.04f), new Vector3(hw + 0.04f, h + 0.1f, hl + 0.04f), Trim);
                }
                else
                {
                    // a block of ice: a darker foot, faintly glowing ice, a cap of snow; one standing on its own
                    // (a square pillar, like Crossfire's) gets a pointed ice top instead
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, 0.16f, hl), IceMid);
                    mb.Box(new Vector3(-hw, 0.16f, -hl), new Vector3(hw, h, hl), new Color(IceLight.r, IceLight.g, IceLight.b, 0.9f));
                    if (hl < hw * 1.5f && h > 1.1f)
                        mb.Frustum(new Vector3(0, h, 0), Mathf.Max(hw, hl) * 1.42f, 0, 0.75f, 4, new Color(IceMid.r, IceMid.g, IceMid.b, 0.8f), Mathf.PI / 4);
                    else
                        mb.Box(new Vector3(-hw - 0.04f, h, -hl - 0.04f), new Vector3(hw + 0.04f, h + 0.1f, hl + 0.04f), Snow);
                }
                break;

            case S3Laser.Kind.Emitter:
                mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h * 0.85f, hl), metal);
                mb.Box(new Vector3(-hw - 0.03f, h * 0.85f, -hl - 0.03f), new Vector3(hw + 0.03f, h * 0.92f, hl + 0.03f), new Color(0.85f, 0.65f, 0.15f));
                mb.Tube(new Vector3(0, 0.45f, hl - 0.02f), new Vector3(0, 0.45f, hl + 0.14f), 0.17f, 0.17f, 10, new Color(0.15f, 0.15f, 0.18f));
                mb.Tube(new Vector3(0, 0.45f, hl + 0.1f), new Vector3(0, 0.45f, hl + 0.16f), 0.11f, 0.11f, 10, new Color(1f, 0.2f, 0.12f, 0f));
                mb.Tube(new Vector3(0, h * 0.92f, -hl * 0.4f), new Vector3(0, h + 0.35f, -hl * 0.4f), 0.03f, 0.02f, 5, metal);
                mb.Icosphere(new Vector3(0, h + 0.38f, -hl * 0.4f), Vector3.one * 0.06f, 0, new Color(1f, 0.25f, 0.15f, 0f));
                for (int i = 0; i < 3; i++)   // cooling fins down both sides
                    foreach (float side in new[] { -1f, 1f })
                    {
                        float y = 0.14f + 0.16f * i;
                        mb.Box(new Vector3(side > 0 ? hw : -hw - 0.05f, y, -hl * 0.7f), new Vector3(side > 0 ? hw + 0.05f : -hw, y + 0.06f, hl * 0.5f), new Color(0.32f, 0.34f, 0.40f));
                    }
                break;

            case S3Laser.Kind.Goal:
                mb.Frustum(Vector3.zero, hw * 0.95f, hw * 0.75f, 0.32f, 8, new Color(0.55f, 0.55f, 0.6f));
                for (int i = 0; i < 4; i++)
                {
                    float a = Mathf.PI / 4 + Mathf.PI / 2 * i;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.Tube(d * hw * 0.55f + Vector3.up * 0.3f, d * hw * 0.42f + Vector3.up * 0.95f, 0.05f, 0.03f, 5, metal);
                }
                mb.Icosphere(new Vector3(0, 0.82f, 0), new Vector3(0.3f, 0.43f, 0.3f), 0, new Color(0.62f, 0.45f, 1f, 0.55f));
                mb.Frustum(new Vector3(0, 0.3f, 0), hw * 0.8f, hw * 0.78f, 0.04f, 16, new Color(0.75f, 0.6f, 1f, 0.45f));   // a glowing rim
                for (int i = 0; i < 4; i++)   // small shards round the base
                {
                    float a = Mathf.PI / 2 * i;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.Tube(d * hw * 0.9f, d * hw * 1.05f + Vector3.up * 0.28f, 0.05f, 0.004f, 5, new Color(0.7f, 0.58f, 1f, 0.6f));
                }
                break;

            case S3Laser.Kind.Mirror:
            {
                // a frozen (fixed) mirror has an ice frame and a cap of snow
                var frame = b.movable ? new Color(0.32f, 0.34f, 0.40f) : IceRoof;
                mb.OrientedBox(new Vector3(0, 0.73f, 0), Vector3.right, Vector3.up, Vector3.forward, new Vector3(hw - 0.06f, 0.53f, 0.025f), new Color(0.80f, 0.90f, 1f, 0.7f));
                if (!b.movable)
                {
                    mb.Box(new Vector3(-hw - 0.05f, h, -0.09f), new Vector3(hw + 0.05f, h + 0.14f, 0.09f), Snow);
                    // icicles along the top bar
                    for (int i = 0; i < 6; i++)
                    {
                        float x = Mathf.Lerp(-hw + 0.14f, hw - 0.14f, i / 5f), len = 0.1f + 0.07f * ((i * 5) % 3);
                        mb.Tube(new Vector3(x, h - 0.06f, 0.07f), new Vector3(x, h - 0.06f - len, 0.07f), 0.03f, 0.004f, 5, new Color(IceRoof.r, IceRoof.g, IceRoof.b, 0.75f));
                        mb.Tube(new Vector3(x, h - 0.06f, -0.07f), new Vector3(x, h - 0.06f - len, -0.07f), 0.03f, 0.004f, 5, new Color(IceRoof.r, IceRoof.g, IceRoof.b, 0.75f));
                    }
                }
                mb.Box(new Vector3(-hw, 0.12f, -0.05f), new Vector3(-hw + 0.07f, h, 0.05f), frame);
                mb.Box(new Vector3(hw - 0.07f, 0.12f, -0.05f), new Vector3(hw, h, 0.05f), frame);
                mb.Box(new Vector3(-hw, h - 0.07f, -0.05f), new Vector3(hw, h, 0.05f), frame);
                mb.Box(new Vector3(-hw, 0.12f, -0.05f), new Vector3(hw, 0.2f, 0.05f), frame);
                mb.Box(new Vector3(-hw, 0, -0.28f), new Vector3(-hw + 0.12f, 0.12f, 0.28f), frame);
                mb.Box(new Vector3(hw - 0.12f, 0, -0.28f), new Vector3(hw, 0.12f, 0.28f), frame);
                break;
            }

            case S3Laser.Kind.Diffractor:
                mb.Frustum(Vector3.zero, hw * 0.9f, hw * 0.7f, 0.2f, 8, metal);
                mb.Frustum(new Vector3(0, 0.2f, 0), 0.06f, 0.05f, 0.14f, 6, metal);
                mb.Icosphere(new Vector3(0, 0.45f, 0), new Vector3(0.21f, 0.26f, 0.21f), 0, new Color(1f, 0.82f, 0.4f, 0.5f));
                for (int i = 0; i < 3; i++)   // prongs holding the orb
                {
                    float a = 2 * Mathf.PI * i / 3;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.Tube(d * 0.12f + Vector3.up * 0.2f, d * 0.2f + Vector3.up * 0.5f, 0.025f, 0.015f, 5, new Color(0.85f, 0.65f, 0.15f));
                }
                break;
        }
        return mb.ToMesh($"S3 {b.kind}");
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

    /// <summary>
    /// A column of light fading upwards with a ring at its foot (additive glow material): marks where a ball hit
    /// straight over the pole comes down, its antipode.
    /// </summary>
    public static Mesh BeaconMesh()
    {
        var mb = new MeshBuilder(0.5f);
        const int n = 10;
        const float H = 7f;
        for (int i = 0; i < n; i++)
        {
            float u0 = (float)i / n, u1 = (float)(i + 1) / n;
            mb.Frustum(new Vector3(0, H * u0, 0), Mathf.Lerp(0.2f, 0.06f, u0), Mathf.Lerp(0.2f, 0.06f, u1), H / n, 10,
                       new Color(0.55f, 0.9f, 1f, 0.55f * (1 - u0) * (1 - u0)));
        }
        const int seg = 32;
        for (int i = 0; i < seg; i++)
        {
            float a0 = 2 * Mathf.PI * i / seg, a1 = 2 * Mathf.PI * (i + 1) / seg;
            Vector3 D(float a, float r) => new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r);
            mb.Quad(D(a0, 0.62f), D(a1, 0.62f), D(a1, 0.74f), D(a0, 0.74f), new Color(0.55f, 0.9f, 1f, 0.7f));
        }
        return mb.ToMesh("S3 Beacon");
    }

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

    /// <summary>A flat ring lying on the ground around local origin; resize it with <see cref="SetRing"/>.</summary>
    public static Mesh RingMesh(int segments, Color col)
    {
        var m = new Mesh { name = "S3 Ring" };
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        for (int i = 0; i < segments; i++)
        {
            normals.Add(Vector3.up); normals.Add(Vector3.up);
            colors.Add(col); colors.Add(col);
            int a = 2 * i, b = 2 * ((i + 1) % segments);
            tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
        }
        SetRing(m, 1, 0.1f, segments);
        m.SetNormals(normals);
        m.SetColors(colors);
        m.SetTriangles(tris, 0, false);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // S^3 coordinates: never cull
        return m;
    }

    static readonly List<Vector3> ringVerts = new List<Vector3>();

    /// <summary>Rewrites a <see cref="RingMesh"/>'s vertices for a new radius and width (metres).</summary>
    public static void SetRing(Mesh m, float radius, float width, int segments = -1)
    {
        if (segments < 0) segments = m.vertexCount / 2;
        ringVerts.Clear();
        float r0 = Mathf.Max(radius - width / 2, 0), r1 = radius + width / 2;
        for (int i = 0; i < segments; i++)
        {
            float a = 2 * Mathf.PI * i / segments;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            ringVerts.Add(d * r0 + Vector3.up * 0.03f);
            ringVerts.Add(d * r1 + Vector3.up * 0.03f);
        }
        m.SetVertices(ringVerts, 0, ringVerts.Count, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
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
