using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly props for the three maps:
///   * Meadow: cottages, round and pine trees, rocks, bushes, toadstools, flowers and grass, in the
///     style of Hyperbolica.
///   * Desert: an adobe fort on a stone platform, palms, cacti, grass oases, boulders, dunes and
///     ruins, after the golden-hour desert concept.
///   * Aurora Basin: an ice castle, an igloo, a snowman, snowy pines, crystals and drifts.
/// Each prop is authored in plain local metres, placed on the ground great-sphere by a frame,
/// and baked into one shared scenery mesh. Props also register their collision cylinder, a baked
/// contact shadow and (if big) a see-through slot with the world. Every surface carries an S3Mat,
/// which S3/Object draws with its own procedural texture.
///
/// Where props stand comes from one random stream per map; each prop takes exactly as many numbers
/// from it as it always has (see Detail), so the courses keep their layout as props gain detail.
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

    /// <summary>
    /// A random source for one prop's looks that takes exactly count numbers from the placement
    /// stream, as many as that prop's simpler build used to, so nothing else in the world moves.
    /// </summary>
    static System.Random Detail(System.Random rng, int count)
    {
        int h = 17;
        for (int i = 0; i < count; i++) h = unchecked(h * 31 + (int)(rng.NextDouble() * 1e6));
        return new System.Random(h);
    }

    /// <summary>Scales a colour's rgb (Color * float would scale alpha too, which S3/Object reads as glow).</summary>
    static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

    /// <summary>Foliage lighter on top, darker underneath (direction is a unit vector from the centre).</summary>
    static System.Func<Vector3, Color> TopLit(Color c, float range = 0.32f) => dir => Mul(c, 1 - range * 0.5f + range * Mathf.Clamp01(dir.y * 0.5f + 0.5f));

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
            if (level.diffractors > 0 && level.Goal != null) keep.Add(new KeepOut(level.Goal.position, 3f));   // the diffracted light comes down here
        }
        if (world.biome == S3World.Biome.Desert)   // an open arena for the puzzles
            keep.Add(new KeepOut(world.LocalFrame(world.puzzleFrame.x, world.puzzleFrame.y, world.puzzleFrame.z) *
                                 S3.GroundPoint(0.5f, 4.5f, world.radius), 8.5f));
        return keep;
    }

    /// <summary>
    /// A landmark at its preferred spot, or the nearest clear spot to it (spiralling outwards), so
    /// fixed buildings never land on a puzzle's light. Returns false if nothing nearby is clear.
    /// </summary>
    static bool FixedSpot(S3World world, List<KeepOut> keep, float x, float z, float yawDeg, float radius, out Matrix4x4 frame)
    {
        float R = world.radius;
        for (int k = 0; k < 160; k++)
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
            House(mb, w, d, h, rh, (int)hd.w);
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
            var mb = new MeshBuilder(0.6f) { material = S3Mat.Stone };
            float g = 0.72f + 0.12f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.35f, 0), new Vector3(size, size * 0.7f, size * 0.9f), 2,
                         new Color(g, g, g * 0.98f), Detail(rng, 42), 0.05f, TopLit(new Color(g, g, g * 0.98f), 0.18f), lumps: 0.35f);
            Add(world, scene, mb, frame, size, size, 0, 0.3f, size * 0.85f, size, size * 0.9f, size, 0.7f);
            keep.Add(new KeepOut(frame.GetColumn(3), size + 0.3f));
        }

        // mushrooms
        for (int i = 0; i < 26; i++)
        {
            if (!TryPlace(rng, R, keep, 0.4f, out var frame)) continue;
            var mb = new MeshBuilder(0.3f);
            int n = 1 + rng.Next(3);
            for (int k = 0; k < n; k++)
            {
                var o = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.5f;
                float s = 0.7f + 0.6f * (float)rng.NextDouble();
                Toadstool(mb, o, s);
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

        // extras, from their own stream so the layout above never changes
        var extra = new System.Random(world.seed * 7919 + 1);
        for (int i = 0; i < 18; i++)   // bushes
        {
            float s = 0.7f + 0.5f * (float)extra.NextDouble();
            if (!TryPlace(extra, R, keep, 0.9f * s, out var frame)) continue;
            var mb = new MeshBuilder(0.4f);
            Bush(mb, extra, s, extra.NextDouble() < 0.4);
            Add(world, scene, mb, frame, sway: 0.2f, occlusion: 0.35f, obstacleRadius: 0.5f * s, obstacleHeight: 0.9f * s,
                shadowRadius: 0.8f * s, shadowHeight: 1f * s, shadowStrength: 0.7f);
            keep.Add(new KeepOut(frame.GetColumn(3), 0.9f * s));
        }
        for (int i = 0; i < 70; i++)   // clumps of wildflowers
        {
            if (!TryPlace(extra, R, keep, 0.3f, out var centre)) continue;
            var palette = FlowerColours[extra.Next(FlowerColours.Length)];
            int n = 3 + extra.Next(6);
            for (int k = 0; k < n; k++)
            {
                if (!TryPlaceNear(extra, R, centre, 0, 0.9f, keep, 0.1f, out var frame, attempts: 3)) continue;
                var mb = new MeshBuilder(0.3f);
                Flower(mb, extra, palette);
                Add(world, scene, mb, frame, sway: 0.8f, occlusion: 0.3f);
            }
        }
        return scene;
    }

    static readonly Color[] FlowerColours =
    {
        new Color(0.98f, 0.97f, 0.94f), new Color(1.0f, 0.84f, 0.2f), new Color(0.72f, 0.52f, 0.95f),
        new Color(0.98f, 0.55f, 0.72f), new Color(0.95f, 0.32f, 0.22f), new Color(0.45f, 0.62f, 0.98f),
    };

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
            var mb = new MeshBuilder(1.2f) { material = S3Mat.Sand };
            float g = (float)rng.NextDouble() * 0.04f;
            mb.Icosphere(new Vector3(0, -0.35f * ry, 0), new Vector3(rx, ry, rz), 3, new Color(Sand.r + g, Sand.g + g, Sand.b + g),
                         Detail(rng, 162), 0.01f, smooth: true, lumps: 0.12f);
            Add(world, scene, mb, frame, rx, ry, 0, 0, Mathf.Min(rx, rz) * 0.7f, ry * 0.6f);
            keep.Add(new KeepOut(frame.GetColumn(3), rx));
        }

        // boulders (dark, like the concept's foreground rocks)
        for (int i = 0; i < 18; i++)
        {
            float size = 0.5f + 1.4f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f) { material = S3Mat.Sandstone };
            float g = 0.38f + 0.1f * (float)rng.NextDouble();
            var rock = new Color(g * 1.12f, g, g * 0.92f);
            mb.Icosphere(new Vector3(0, size * 0.3f, 0), new Vector3(size * 1.2f, size * 0.8f, size), 2,
                         rock, Detail(rng, 42), 0.06f, TopLit(rock, 0.2f), lumps: 0.4f);
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

        // extras, from their own stream so the layout above never changes
        var extra = new System.Random(world.seed * 7919 + 2);
        for (int i = 0; i < 9; i++)   // cacti out on the sand
        {
            if (!TryPlace(extra, R, keep, 1f, out var frame)) continue;
            var mb = new MeshBuilder(0.4f);
            float h = Cactus(mb, extra);
            Add(world, scene, mb, frame, 0.6f, h, 0, 0.3f, 0.3f, h, 0.6f, h, 0.7f);
            keep.Add(new KeepOut(frame.GetColumn(3), 1f));
        }
        for (int i = 0; i < 14; i++)   // pots and jars
        {
            if (!TryPlace(extra, R, keep, 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.3f);
            Jars(mb, extra);
            Add(world, scene, mb, frame, occlusion: 0.3f, obstacleRadius: 0.35f, obstacleHeight: 0.7f, shadowRadius: 0.45f, shadowHeight: 0.6f, shadowStrength: 0.6f);
            keep.Add(new KeepOut(frame.GetColumn(3), 0.6f));
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

    // ================================================================== aurora basin

    static readonly Color Snow = new Color(0.94f, 0.96f, 0.99f);
    static readonly Color SnowShade = new Color(0.79f, 0.85f, 0.93f);
    static readonly Color PineGreen = new Color(0.18f, 0.36f, 0.31f);
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

        // landmarks, after the concept: ice castle across the lake, an igloo by the shore, a snowman by the first tee
        if (FixedSpot(world, keep, -12.5f, 22f, 35, 5.5f, out var f))
        {
            var mb = new MeshBuilder();
            IceCastle(mb);
            Add(world, scene, mb, f, 4.6f, 9f, 0, 0.2f, 3.8f, 8f, 4.2f, 6f);
            keep.Add(new KeepOut(f.GetColumn(3), 5.5f));
        }
        if (FixedSpot(world, keep, 7.5f, 14f, -160, 2.6f, out f))
        {
            var mb = new MeshBuilder();
            Igloo(mb);
            Add(world, scene, mb, f, 2.2f, 2.2f, 0, 0.25f, 1.8f, 1.8f, 2f, 1.8f);
            keep.Add(new KeepOut(f.GetColumn(3), 2.8f));
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
            }
        }

        // snowy pines all over the world
        for (int i = 0; i < 46; i++)
        {
            if (!TryPlace(rng, R, keep, 1.8f, out var frame)) continue;
            var mb = new MeshBuilder();
            float s = 0.85f + 0.45f * (float)rng.NextDouble();
            SnowyPine(mb, rng, s);
            Add(world, scene, mb, frame, 1.5f * s, 4.8f * s, 0.15f, 0.35f, 0.3f * s, 6f, 1.4f * s, 4f * s);
            keep.Add(new KeepOut(frame.GetColumn(3), 1.6f * s));
        }

        // crystal spires
        for (int i = 0; i < 14; i++)
        {
            if (!TryPlace(rng, R, keep, 1.2f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f);
            float h = Crystals(mb, rng);
            Add(world, scene, mb, frame, 1.1f, h, 0, 0.2f, 0.6f, h, 1f, h, 0.6f);
            keep.Add(new KeepOut(frame.GetColumn(3), 1.3f));
        }

        // snowdrifts
        for (int i = 0; i < 12; i++)
        {
            float rx = 2.2f + 1.5f * (float)rng.NextDouble(), rz = 1.2f + 0.8f * (float)rng.NextDouble();
            if (!TryPlace(rng, R, keep, rx + 0.8f, out var frame)) continue;
            var mb = new MeshBuilder(1.2f) { material = S3Mat.Snow };
            mb.Icosphere(new Vector3(0, -0.25f, 0), new Vector3(rx, 0.75f, rz), 3, Snow, Detail(rng, 162), 0.01f, smooth: true, lumps: 0.15f);
            Add(world, scene, mb, frame, rx, 0.6f, 0, 0, Mathf.Min(rx, rz) * 0.7f, 0.5f, rx * 0.8f, 0.5f, 0.4f);
            keep.Add(new KeepOut(frame.GetColumn(3), rx));
        }

        // dark rocks with snow caps
        for (int i = 0; i < 16; i++)
        {
            float size = 0.45f + 1.1f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f) { material = S3Mat.Stone };
            float g = 0.30f + 0.06f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.3f, 0), new Vector3(size * 1.1f, size * 0.75f, size), 2,
                         new Color(g, g * 1.05f, g * 1.2f), Detail(rng, 42), 0.05f,
                         c => c.y > 0.42f ? Snow : new Color(g, g * 1.05f, g * 1.2f), lumps: 0.35f,
                         faceMaterial: c => c.y > 0.42f ? S3Mat.Snow : S3Mat.Stone);
            Add(world, scene, mb, frame, size * 1.1f, size, 0, 0.3f, size * 0.95f, size, size, size, 0.8f);
            keep.Add(new KeepOut(frame.GetColumn(3), size + 0.4f));
        }

        // frosted reeds poking through the snow
        for (int i = 0; i < 260; i++)
        {
            if (!TryPlace(rng, R, keep, 0.2f, out var frame, attempts: 3)) continue;
            var mb = new MeshBuilder(1f);
            FrostTuft(mb, rng);
            Add(world, scene, mb, frame, sway: 0.6f, occlusion: 0.25f);
        }
        return scene;
    }


    static void SnowyPine(MeshBuilder mb, System.Random rng, float s)
    {
        var r = Detail(rng, 1);
        mb.material = S3Mat.Bark;
        mb.Frustum(Vector3.zero, 0.2f * s, 0.14f * s, 1.1f * s, 8, new Color(0.35f, 0.25f, 0.19f));
        float g = (float)r.NextDouble() * 0.05f;
        var needles = new Color(PineGreen.r + g, PineGreen.g + g, PineGreen.b + g);
        for (int k = 0; k < 4; k++)
        {
            float y = (0.75f + 0.75f * k) * s, rad = (1.5f - 0.3f * k) * s, h = 1.5f * s, phase = k * 0.4f + 0.3f * (float)r.NextDouble();
            mb.material = S3Mat.Needles;
            mb.Frustum(new Vector3(0, y, 0), rad, 0, h, 9, Mul(needles, 0.94f + 0.06f * k), phase);
            mb.material = S3Mat.Snow;
            mb.Frustum(new Vector3(0, y + h * 0.42f, 0), rad * 0.6f, 0, h * 0.58f + 0.02f, 9, Snow, phase);
        }
        mb.material = S3Mat.Plain;
    }


    /// <summary>A cluster of tilted hexagonal ice prisms with shards around the base. Returns its height.</summary>
    static float Crystals(MeshBuilder mb, System.Random rng)
    {
        var cols = new[] { new Color(0.62f, 0.89f, 0.96f, 0.75f), new Color(0.43f, 0.76f, 0.90f, 0.75f), new Color(0.77f, 0.95f, 0.98f, 0.75f) };
        int n = 3 + rng.Next(3);
        float top = 0;
        mb.material = S3Mat.Ice;
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
        // small shards, from the cluster's own numbers so the placement stream is untouched
        var shards = new System.Random(Mathf.RoundToInt(top * 1e4f) + n);
        for (int i = 0; i < 4; i++)
        {
            float a = 2 * Mathf.PI * (float)shards.NextDouble(), h = 0.3f + 0.4f * (float)shards.NextDouble();
            var basePt = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (0.55f + 0.25f * (float)shards.NextDouble());
            var tip = basePt + new Vector3(Mathf.Cos(a) * 0.45f * h, h, Mathf.Sin(a) * 0.45f * h);
            mb.Tube(basePt, tip, 0.08f, 0.01f, 6, cols[(i + 1) % cols.Length]);
        }
        mb.material = S3Mat.Plain;
        return top;
    }


    static void Igloo(MeshBuilder mb)
    {
        const float r = 1.7f;
        mb.material = S3Mat.IceBlock;
        for (int k = 0; k < 7; k++)
        {
            float a0 = Mathf.PI / 2 * k / 7, a1 = Mathf.PI / 2 * (k + 1) / 7;
            mb.Frustum(new Vector3(0, r * Mathf.Sin(a0), 0), r * Mathf.Cos(a0), Mathf.Max(r * Mathf.Cos(a1), 0.01f),
                       r * (Mathf.Sin(a1) - Mathf.Sin(a0)), 16, k % 2 == 1 ? new Color(0.89f, 0.93f, 0.97f) : new Color(0.85f, 0.89f, 0.94f), k * 0.2f);
        }
        mb.Box(new Vector3(-0.5f, 0, -2.5f), new Vector3(0.5f, 0.9f, -1.2f), new Color(0.89f, 0.93f, 0.97f));
        mb.material = S3Mat.Plain;
        mb.Quad(new Vector3(-0.32f, 0, -2.52f), new Vector3(0.32f, 0, -2.52f), new Vector3(0.32f, 0.7f, -2.52f), new Vector3(-0.32f, 0.7f, -2.52f), new Color(0.23f, 0.31f, 0.43f));
        // a warm glow from inside
        mb.Quad(new Vector3(-0.24f, 0.02f, -2.53f), new Vector3(0.24f, 0.02f, -2.53f), new Vector3(0.24f, 0.45f, -2.53f), new Vector3(-0.24f, 0.45f, -2.53f), new Color(1f, 0.62f, 0.3f, 0.45f));
    }


    static void IceCastle(MeshBuilder mb)
    {
        void Tower(float x, float z, float r, float h)
        {
            mb.material = S3Mat.IceBlock;
            mb.Frustum(new Vector3(x, 0, z), r, r * 0.9f, h, 8, IceLight, Mathf.PI / 8);
            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.PI / 8 + 2 * Mathf.PI * i / 8;
                float cx = x + Mathf.Cos(a) * r * 0.85f, cz = z + Mathf.Sin(a) * r * 0.85f;
                mb.Box(new Vector3(cx - 0.16f, h, cz - 0.16f), new Vector3(cx + 0.16f, h + 0.4f, cz + 0.16f), IceMid);
            }
            mb.material = S3Mat.Ice;
            mb.Frustum(new Vector3(x, h + 0.1f, z), r * 0.75f, 0, r * 1.4f, 8, IceRoof, Mathf.PI / 8);
            mb.Icosphere(new Vector3(x, h + 0.1f + r * 1.4f + 0.08f, z), Vector3.one * 0.12f, 1, new Color(0.8f, 0.95f, 1f, 0.4f), smooth: true);
        }
        Tower(0, 0, 1.6f, 7); Tower(-3.4f, 0.4f, 1f, 4.6f); Tower(3.2f, 0.2f, 1f, 5.2f);
        mb.material = S3Mat.IceBlock;
        mb.Box(new Vector3(-3, 0, -0.5f), new Vector3(3, 2.8f, 0.6f), IceMid);
        for (float x = -2.8f; x < 2.8f; x += 0.7f) mb.Box(new Vector3(x, 2.8f, -0.5f), new Vector3(x + 0.35f, 3.2f, 0.6f), IceLight);
        mb.material = S3Mat.Plain;
        mb.Quad(new Vector3(-0.45f, 0, -0.52f), new Vector3(0.45f, 0, -0.52f), new Vector3(0.45f, 1.9f, -0.52f), new Vector3(-0.45f, 1.9f, -0.52f), IceDoor);
        mb.Quad(new Vector3(-0.25f, 4.5f, -1.62f), new Vector3(0.25f, 4.5f, -1.62f), new Vector3(0.25f, 5.3f, -1.62f), new Vector3(-0.25f, 5.3f, -1.62f), IceDoor);
        // icicles along the wall's front
        mb.material = S3Mat.Ice;
        for (int i = 0; i < 14; i++)
        {
            float ix = -2.8f + 0.4f * i, len = 0.25f + 0.25f * Mathf.Abs(Mathf.Sin(i * 2.7f));
            mb.Frustum(new Vector3(ix, 2.8f - len, -0.53f), 0.0f, 0.05f, len, 5, new Color(0.85f, 0.96f, 1f, 0.7f));
        }
        mb.material = S3Mat.Plain;
    }


    static void Snowman(MeshBuilder mb)
    {
        var w = new Color(0.96f, 0.97f, 0.99f);
        var black = new Color(0.11f, 0.11f, 0.13f);
        mb.material = S3Mat.Snow;
        mb.Icosphere(new Vector3(0, 0.55f, 0), new Vector3(0.6f, 0.55f, 0.6f), 2, w, smooth: true);
        mb.Icosphere(new Vector3(0, 1.38f, 0), new Vector3(0.44f, 0.42f, 0.44f), 2, w, smooth: true);
        mb.Icosphere(new Vector3(0, 2.02f, 0), new Vector3(0.32f, 0.32f, 0.32f), 2, w, smooth: true);
        mb.material = S3Mat.Cloth;
        var scarf = new Color(0.90f, 0.31f, 0.24f);
        mb.Frustum(new Vector3(0, 1.7f, 0), 0.36f, 0.36f, 0.14f, 12, scarf);
        mb.Quad(new Vector3(0.12f, 1.78f, -0.33f), new Vector3(0.26f, 1.78f, -0.3f), new Vector3(0.3f, 1.3f, -0.42f), new Vector3(0.16f, 1.3f, -0.45f), scarf);
        mb.material = S3Mat.Paint;
        mb.Tube(new Vector3(0, 2.04f, -0.28f), new Vector3(0, 2.0f, -0.68f), 0.07f, 0.01f, 8, new Color(0.94f, 0.54f, 0.17f));
        foreach (float x in new[] { -0.11f, 0.11f }) mb.Icosphere(new Vector3(x, 2.14f, -0.28f), Vector3.one * 0.04f, 1, black, smooth: true);
        foreach (float y in new[] { 1.25f, 1.45f }) mb.Icosphere(new Vector3(0, y, -0.43f), Vector3.one * 0.04f, 1, black, smooth: true);
        // stick arms with twig fingers
        mb.material = S3Mat.Bark;
        var stick = new Color(0.36f, 0.26f, 0.18f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Vector3 a = new Vector3(0.38f * sx, 1.45f, 0), b = new Vector3(0.72f * sx, 1.6f, -0.04f);
            mb.Tube(a, b, 0.022f, 0.014f, 5, stick);
            mb.Tube(b, b + new Vector3(0.08f * sx, 0.1f, 0), 0.01f, 0.005f, 4, stick);
            mb.Tube(b, b + new Vector3(0.11f * sx, -0.02f, 0.02f), 0.01f, 0.005f, 4, stick);
        }
        mb.material = S3Mat.Plain;
    }


    static void FrostTuft(MeshBuilder mb, System.Random rng)
    {
        mb.material = S3Mat.Grass;
        int blades = 3 + rng.Next(3);
        float g = (float)rng.NextDouble();
        var col = new Color(0.62f + 0.1f * g, 0.66f + 0.08f * g, 0.58f);
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float h = 0.25f + 0.3f * (float)rng.NextDouble();
            Vector3 tip = dir * (0.06f + 0.1f * (float)rng.NextDouble()) + Vector3.up * h;
            Blade(mb, Vector3.zero, tip, 0.025f, col);
        }
        mb.material = S3Mat.Plain;
    }

    /// <summary>
    /// Additive glow scenery: the Basin's aurora, curtains looped around the pole 7-13 m above a wavy
    /// great circle of the ground (the pole is straight up from everywhere, so it is always overhead).
    /// </summary>
    public static MeshBuilder BuildGlow(S3World world)
    {
        if (world.biome != S3World.Biome.Basin) return null;
        float R = world.radius;
        var mb = new MeshBuilder();
        Vector4 A = S3.GroundPoint(-13, 13, R), P2 = S3.GroundPoint(13, 14, R);
        Vector4 B = S3.Ortho(P2, A);
        Vector4 C = Vector4.zero;
        foreach (var e in new[] { S3.EX, S3.EZ, S3.EW })
        {
            Vector4 v = S3.Ortho(e, A, B);
            if (v.sqrMagnitude > C.sqrMagnitude) C = v;
        }
        Vector4 U(float th) => (Mathf.Cos(th) * A + Mathf.Sin(th) * B + 0.16f * Mathf.Sin(3 * th) * C).normalized;
        float Wob(float th) => 1.2f * Mathf.Sin(th * 7) + 0.6f * Mathf.Sin(th * 13 + 1);
        var bands = new (float h0, float h1, Color c, float a)[]
        {
            (7f, 8.6f, new Color(0.25f, 0.89f, 0.61f), 0.66f),
            (8.6f, 10.4f, new Color(0.27f, 0.84f, 0.71f), 0.5f),
            (10.4f, 12f, new Color(0.50f, 0.50f, 0.94f), 0.34f),
            (12f, 13.2f, new Color(0.63f, 0.44f, 0.94f), 0.16f),
        };
        const int steps = 260;
        float bottom = bands[0].h0, top = bands[bands.Length - 1].h1;
        for (int i = 0; i < steps; i++)
        {
            float t0 = 2 * Mathf.PI * i / steps, t1 = 2 * Mathf.PI * (i + 1) / steps;
            Vector4 f0 = U(t0), f1 = U(t1);
            float flick = 0.65f + 0.35f * Mathf.Sin(i * 0.9f) * Mathf.Sin(i * 0.23f);
            foreach (var (h0, h1, c, a) in bands)
            {
                var col = new Color(c.r, c.g, c.b, a * flick);
                float v0 = (h0 - bottom) / (top - bottom), v1 = (h1 - bottom) / (top - bottom);
                mb.QuadS3(S3.Lift(f0, (h0 + Wob(t0)) / R), S3.Lift(f1, (h0 + Wob(t1)) / R),
                          S3.Lift(f1, (h1 + Wob(t1)) / R), S3.Lift(f0, (h1 + Wob(t0)) / R), col, R,
                          new Vector4(t0, v0, 0, 1), new Vector4(t1, v0, 0, 1), new Vector4(t1, v1, 0, 1), new Vector4(t0, v1, 0, 1));
            }
        }
        return mb;
    }

    /// <summary>
    /// Ambient particles for a map, drawn by S3/Particles in a box around the eye: pollen and
    /// dandelion fluff over the meadow, sunlit dust in the desert, falling snow in the basin.
    /// box: the box's size in metres (xyz) and whether the sun lights the particles (w).
    /// </summary>
    public static Mesh AmbientParticles(S3World.Biome biome, float R, out Vector4 box)
    {
        var rng = new System.Random(4242 + (int)biome);
        int count; Color col; float size0, size1, opacity, flutter;
        Vector3 wind, spread;
        switch (biome)
        {
            case S3World.Biome.Desert:
                count = 700; col = new Color(1f, 0.86f, 0.62f); size0 = 0.012f; size1 = 0.028f; opacity = 0.15f;
                wind = new Vector3(0.7f, 0.02f, 0.25f); spread = new Vector3(0.3f, 0.06f, 0.3f); flutter = 0.8f;
                box = new Vector4(16, 7, 16, 1);
                break;
            case S3World.Biome.Basin:
                count = 2600; col = new Color(1f, 1f, 1f); size0 = 0.02f; size1 = 0.045f; opacity = 0.9f;
                wind = new Vector3(0.35f, -0.85f, 0.15f); spread = new Vector3(0.2f, 0.25f, 0.2f); flutter = 1.6f;
                box = new Vector4(18, 10, 18, 1);
                break;
            default:
                count = 420; col = new Color(1f, 0.98f, 0.86f); size0 = 0.018f; size1 = 0.035f; opacity = 0.25f;
                wind = new Vector3(0.3f, 0.05f, 0.18f); spread = new Vector3(0.15f, 0.08f, 0.15f); flutter = 0.9f;
                box = new Vector4(14, 6, 14, 0);
                break;
        }
        var verts = new List<Vector3>(count * 4);
        var colors = new List<Color>(count * 4);
        var seeds = new List<Vector4>(count * 4);
        var vels = new List<Vector4>(count * 4);
        var tris = new List<int>(count * 6);
        var corners = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        for (int i = 0; i < count; i++)
        {
            var seed = new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(),
                                   Mathf.Lerp(size0, size1, (float)rng.NextDouble()));
            var v = wind + Vector3.Scale(spread, new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1));
            var vel = new Vector4(v.x, v.y, v.z, flutter * (0.6f + 0.8f * (float)rng.NextDouble()));
            var c = new Color(col.r, col.g, col.b, opacity * (0.7f + 0.3f * (float)rng.NextDouble()));
            int start = verts.Count;
            foreach (var corner in corners) { verts.Add(corner); colors.Add(c); seeds.Add(seed); vels.Add(vel); }
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
        }
        var m = new Mesh { name = "S3 Particles" };
        m.SetVertices(verts);
        m.SetColors(colors);
        m.SetUVs(1, seeds);
        m.SetUVs(2, vels);
        m.SetTriangles(tris, 0, false);
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return m;
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
                    mb.material = S3Mat.Ice;
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h, hl), new Color(0.70f, 0.90f, 1f, 0.8f));
                    mb.Box(new Vector3(-hw * 0.6f, h, -hl * 0.8f), new Vector3(hw * 0.6f, h + 0.18f, hl * 0.8f), new Color(0.85f, 0.97f, 1f, 0.7f));
                }
                else if (biome == S3World.Biome.Desert)
                {
                    mb.material = S3Mat.Adobe;
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h, hl), Adobe);
                    mb.material = S3Mat.Brick;
                    mb.Box(new Vector3(-hw - 0.04f, h, -hl - 0.04f), new Vector3(hw + 0.04f, h + 0.1f, hl + 0.04f), Trim);
                }
                else
                {
                    mb.material = S3Mat.IceBlock;
                    mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h, hl), IceLight);
                    mb.material = S3Mat.Snow;
                    mb.Box(new Vector3(-hw - 0.04f, h, -hl - 0.04f), new Vector3(hw + 0.04f, h + 0.1f, hl + 0.04f), Snow);
                }
                break;

            case S3Laser.Kind.Emitter:
                mb.material = S3Mat.Metal;
                mb.Box(new Vector3(-hw, 0, -hl), new Vector3(hw, h * 0.85f, hl), metal);
                mb.material = S3Mat.Paint;
                mb.Box(new Vector3(-hw - 0.03f, h * 0.85f, -hl - 0.03f), new Vector3(hw + 0.03f, h * 0.92f, hl + 0.03f), new Color(0.85f, 0.65f, 0.15f));
                mb.material = S3Mat.Metal;
                mb.Tube(new Vector3(0, 0.45f, hl - 0.02f), new Vector3(0, 0.45f, hl + 0.14f), 0.17f, 0.17f, 12, new Color(0.15f, 0.15f, 0.18f));
                mb.material = S3Mat.Plain;
                mb.Tube(new Vector3(0, 0.45f, hl + 0.1f), new Vector3(0, 0.45f, hl + 0.16f), 0.11f, 0.11f, 12, new Color(1f, 0.2f, 0.12f, 0f));
                mb.material = S3Mat.Metal;
                mb.Tube(new Vector3(0, h * 0.92f, -hl * 0.4f), new Vector3(0, h + 0.35f, -hl * 0.4f), 0.03f, 0.02f, 6, metal);
                mb.material = S3Mat.Plain;
                mb.Icosphere(new Vector3(0, h + 0.38f, -hl * 0.4f), Vector3.one * 0.06f, 1, new Color(1f, 0.25f, 0.15f, 0f), smooth: true);
                break;

            case S3Laser.Kind.Goal:
                mb.material = S3Mat.Stone;
                mb.Frustum(Vector3.zero, hw * 0.95f, hw * 0.75f, 0.32f, 10, new Color(0.55f, 0.55f, 0.6f));
                mb.material = S3Mat.Metal;
                for (int i = 0; i < 4; i++)
                {
                    float a = Mathf.PI / 4 + Mathf.PI / 2 * i;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    mb.Tube(d * hw * 0.55f + Vector3.up * 0.3f, d * hw * 0.42f + Vector3.up * 0.95f, 0.05f, 0.03f, 6, metal);
                }
                mb.material = S3Mat.Ice;
                mb.Icosphere(new Vector3(0, 0.82f, 0), new Vector3(0.3f, 0.43f, 0.3f), 1, new Color(0.62f, 0.45f, 1f, 0.55f));
                break;

            case S3Laser.Kind.Mirror:
            {
                var frame = new Color(0.32f, 0.34f, 0.40f);
                mb.material = S3Mat.Glass;
                mb.OrientedBox(new Vector3(0, 0.73f, 0), Vector3.right, Vector3.up, Vector3.forward, new Vector3(hw - 0.06f, 0.53f, 0.025f), new Color(0.80f, 0.90f, 1f, 0.7f));
                mb.material = S3Mat.Metal;
                mb.Box(new Vector3(-hw, 0.12f, -0.05f), new Vector3(-hw + 0.07f, h, 0.05f), frame);
                mb.Box(new Vector3(hw - 0.07f, 0.12f, -0.05f), new Vector3(hw, h, 0.05f), frame);
                mb.Box(new Vector3(-hw, h - 0.07f, -0.05f), new Vector3(hw, h, 0.05f), frame);
                mb.Box(new Vector3(-hw, 0.12f, -0.05f), new Vector3(hw, 0.2f, 0.05f), frame);
                mb.Box(new Vector3(-hw, 0, -0.28f), new Vector3(-hw + 0.12f, 0.12f, 0.28f), frame);
                mb.Box(new Vector3(hw - 0.12f, 0, -0.28f), new Vector3(hw, 0.12f, 0.28f), frame);
                break;
            }

            case S3Laser.Kind.Diffractor:
                mb.material = S3Mat.Metal;
                mb.Frustum(Vector3.zero, hw * 0.9f, hw * 0.7f, 0.2f, 10, metal);
                mb.Frustum(new Vector3(0, 0.2f, 0), 0.06f, 0.05f, 0.14f, 8, metal);
                mb.material = S3Mat.Ice;
                mb.Icosphere(new Vector3(0, 0.45f, 0), new Vector3(0.21f, 0.26f, 0.21f), 1, new Color(1f, 0.82f, 0.4f, 0.5f));
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


    // cottage colours: walls, wall ends, roof, roof edge (style 0 keeps the red roof hole 1 plays through)
    static readonly Color[,] CottageColours =
    {
        { new Color(0.90f, 0.83f, 0.70f), new Color(0.82f, 0.73f, 0.60f), new Color(0.86f, 0.12f, 0.09f), new Color(0.62f, 0.07f, 0.05f) },
        { new Color(0.93f, 0.82f, 0.58f), new Color(0.84f, 0.72f, 0.50f), new Color(0.34f, 0.38f, 0.46f), new Color(0.24f, 0.27f, 0.33f) },
        { new Color(0.74f, 0.81f, 0.86f), new Color(0.64f, 0.71f, 0.77f), new Color(0.80f, 0.42f, 0.22f), new Color(0.58f, 0.28f, 0.14f) },
    };

    /// <summary>
    /// Gabled cottage: walls w (x) by d (z), wall height h, roof rise rh. Front faces -z.
    /// Everything is solid: thick plaster walls on a stone plinth, prism gables, tiled roof slabs with a
    /// ridge cap, a brick chimney, painted windows with shutters and a window box, a plank door and a
    /// path of stepping stones.
    /// </summary>
    static void House(MeshBuilder mb, float w, float d, float h, float rh, int style)
    {
        const float T = 0.28f;   // wall thickness
        style = Mathf.Clamp(style, 0, CottageColours.GetLength(0) - 1);
        Color wall = CottageColours[style, 0], wallEnd = CottageColours[style, 1], roof = CottageColours[style, 2], roofEdge = CottageColours[style, 3];
        var stone = new Color(0.62f, 0.60f, 0.57f);
        var paint = new Color(0.95f, 0.93f, 0.88f);
        var shutter = style == 1 ? new Color(0.30f, 0.50f, 0.42f) : new Color(0.27f, 0.42f, 0.60f);
        float x = w / 2, z = d / 2;
        // plinth
        mb.material = S3Mat.Stone;
        mb.Box(new Vector3(-x - 0.1f, 0, -z - 0.1f), new Vector3(x + 0.1f, 0.32f, z + 0.1f), stone);
        // walls (outer faces at +-x, +-z)
        mb.material = S3Mat.Plaster;
        mb.Box(new Vector3(-x, 0, -z), new Vector3(x, h, -z + T), wall);
        mb.Box(new Vector3(-x, 0, z - T), new Vector3(x, h, z), wall);
        mb.Box(new Vector3(-x, 0, -z + T), new Vector3(-x + T, h, z - T), wallEnd);
        mb.Box(new Vector3(x - T, 0, -z + T), new Vector3(x, h, z - T), wallEnd);
        // gables as prisms, flush with the front and back walls
        mb.Prism(new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(0, h + rh, -z), new Vector3(0, 0, T), wall);
        mb.Prism(new Vector3(-x, h, z - T), new Vector3(x, h, z - T), new Vector3(0, h + rh, z - T), new Vector3(0, 0, T), wall);
        // roof: two thick tiled slabs whose planes meet the wall tops exactly, plus a ridge cap
        mb.material = S3Mat.RoofTile;
        float o = 0.45f, eave = h - rh * o / x, zo = z + o;
        mb.Slab(new Vector3(-x - o, eave, -zo), new Vector3(-x - o, eave, zo), new Vector3(0, h + rh, zo), new Vector3(0, h + rh, -zo), 0.2f, roof, roofEdge);
        mb.Slab(new Vector3(x + o, eave, zo), new Vector3(x + o, eave, -zo), new Vector3(0, h + rh, -zo), new Vector3(0, h + rh, zo), 0.2f, roof, roofEdge);
        mb.Box(new Vector3(-0.14f, h + rh + 0.12f, -zo - 0.04f), new Vector3(0.14f, h + rh + 0.3f, zo + 0.04f), roofEdge);
        // brick chimney with a stone cap
        mb.material = S3Mat.Brick;
        mb.Box(new Vector3(x * 0.35f, h + rh * 0.4f, z * 0.3f), new Vector3(x * 0.35f + 0.5f, h + rh + 0.7f, z * 0.3f + 0.5f), new Color(0.66f, 0.38f, 0.28f));
        mb.material = S3Mat.Stone;
        mb.Box(new Vector3(x * 0.35f - 0.06f, h + rh + 0.7f, z * 0.3f - 0.06f), new Vector3(x * 0.35f + 0.56f, h + rh + 0.82f, z * 0.3f + 0.56f), stone);

        Vector3 up = Vector3.up;
        // a painted window standing out of the wall, with mullions, glass and a sill; c is on the outer wall face
        void Window(Vector3 c, Vector3 outward, Vector3 side, float ww, float wh, bool shutters)
        {
            mb.material = S3Mat.Paint;
            mb.OrientedBox(c + outward * 0.04f, side, up, outward, new Vector3(ww / 2 + 0.09f, wh / 2 + 0.09f, 0.04f), paint);
            Vector3 pc = c + outward * 0.083f;
            mb.material = S3Mat.Glass;
            mb.Quad(pc - side * (ww / 2) - up * (wh / 2), pc + side * (ww / 2) - up * (wh / 2),
                    pc + side * (ww / 2) + up * (wh / 2), pc - side * (ww / 2) + up * (wh / 2), Pane);
            mb.material = S3Mat.Paint;
            mb.OrientedBox(c + outward * 0.1f, side, up, outward, new Vector3(0.03f, wh / 2, 0.02f), paint);
            mb.OrientedBox(c + outward * 0.1f, side, up, outward, new Vector3(ww / 2, 0.03f, 0.02f), paint);
            mb.material = S3Mat.Stone;
            mb.OrientedBox(c - up * (wh / 2 + 0.13f) + outward * 0.09f, side, up, outward, new Vector3(ww / 2 + 0.17f, 0.05f, 0.09f), stone);
            if (!shutters) return;
            mb.material = S3Mat.Wood;
            foreach (float sgn in new[] { -1f, 1f })
                mb.OrientedBox(c + side * (sgn * (ww / 2 + 0.33f)) + outward * 0.05f, side, up, outward, new Vector3(0.21f, wh / 2 + 0.06f, 0.025f), shutter);
        }
        float wy = Mathf.Min(1.65f, h - 0.8f);
        // front: door (frame, planks, knob, step), a window with shutters and a window box
        Vector3 doorC = new Vector3(-x * 0.45f, 1.05f, -z);
        mb.material = S3Mat.Paint;
        mb.OrientedBox(doorC + Vector3.back * 0.035f, Vector3.right, up, Vector3.back, new Vector3(0.56f, 1.08f, 0.035f), paint);
        mb.material = S3Mat.Wood;
        mb.OrientedBox(doorC + Vector3.back * 0.09f + up * -0.03f, Vector3.right, up, Vector3.back, new Vector3(0.44f, 1.0f, 0.03f), new Color(0.50f, 0.32f, 0.20f));
        mb.material = S3Mat.Metal;
        mb.Icosphere(doorC + new Vector3(0.3f, -0.05f, -0.15f), Vector3.one * 0.05f, 1, new Color(0.85f, 0.7f, 0.3f), smooth: true);
        mb.material = S3Mat.Stone;
        mb.Box(new Vector3(doorC.x - 0.7f, 0, -z - 0.55f), new Vector3(doorC.x + 0.7f, 0.18f, -z), stone);
        Vector3 frontWin = new Vector3(x * 0.45f, wy, -z);
        Window(frontWin, Vector3.back, Vector3.right, 0.9f, 0.9f, true);
        WindowBox(mb, frontWin - up * 0.66f + Vector3.back * 0.2f, 0.95f);
        Window(new Vector3(0, h + rh * 0.35f, -z), Vector3.back, Vector3.right, 0.55f, 0.55f, false);
        // back
        Window(new Vector3(-x * 0.45f, wy, z), Vector3.forward, Vector3.left, 0.9f, 0.9f, false);
        Window(new Vector3(x * 0.45f, wy, z), Vector3.forward, Vector3.left, 0.9f, 0.9f, false);
        // sides
        for (int sd = -1; sd <= 1; sd += 2)
        {
            Window(new Vector3(-x, wy, sd * z * 0.45f), Vector3.left, Vector3.back, 0.8f, 0.9f, false);
            Window(new Vector3(x, wy, sd * z * 0.45f), Vector3.right, Vector3.forward, 0.8f, 0.9f, false);
        }
        // stepping stones leading away from the door
        mb.material = S3Mat.Stone;
        for (int i = 0; i < 4; i++)
        {
            float sz = -z - 1.05f - 0.68f * i, sx = doorC.x + 0.18f * Mathf.Sin(i * 1.9f);
            mb.Icosphere(new Vector3(sx, -0.02f, sz), new Vector3(0.3f, 0.06f, 0.24f), 1, Mul(stone, 0.95f + 0.05f * (i % 2)));
        }
        mb.material = S3Mat.Plain;
    }

    /// <summary>A planter box of flowers under a window (centre of the box's top, width).</summary>
    static void WindowBox(MeshBuilder mb, Vector3 top, float width)
    {
        mb.material = S3Mat.Wood;
        mb.Box(top + new Vector3(-width / 2, -0.18f, -0.1f), top + new Vector3(width / 2, 0, 0.1f), new Color(0.45f, 0.30f, 0.20f));
        mb.material = S3Mat.Leaves;
        mb.Box(top + new Vector3(-width / 2 + 0.04f, 0, -0.07f), top + new Vector3(width / 2 - 0.04f, 0.06f, 0.07f), new Color(0.24f, 0.50f, 0.20f));
        mb.material = S3Mat.Petal;
        for (int i = 0; i < 6; i++)
        {
            float fx = -width / 2 + 0.1f + (width - 0.2f) * i / 5f;
            var col = FlowerColours[(i * 3 + 1) % FlowerColours.Length];
            mb.Icosphere(top + new Vector3(fx, 0.1f + 0.03f * (i % 2), (i % 2) * 0.04f - 0.02f), new Vector3(0.07f, 0.05f, 0.07f), 0, col);
        }
        mb.material = S3Mat.Plain;
    }


    /// <summary>A broadleaf tree: a stout trunk, two boughs and a crown of three leafy lobes, sometimes with apples.</summary>
    static void RoundTree(MeshBuilder mb, System.Random rng, float s)
    {
        var r = Detail(rng, 127);
        float trunkH = 2.0f * s;
        mb.material = S3Mat.Bark;
        mb.Frustum(Vector3.zero, 0.26f * s, 0.15f * s, trunkH + 0.4f * s, 8, Bark);
        mb.Frustum(Vector3.zero, 0.34f * s, 0.24f * s, 0.25f * s, 8, Mul(Bark, 0.9f));   // root flare
        for (int k = 0; k < 2; k++)
        {
            float a = 2 * Mathf.PI * (float)r.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 from = Vector3.up * (trunkH * (0.62f + 0.18f * k));
            mb.Tube(from, from + (dir * 0.7f + Vector3.up * 0.8f) * s, 0.09f * s, 0.045f * s, 6, Bark);
        }
        float g = (float)r.NextDouble();
        var leaf = new Color(0.20f + 0.08f * g, 0.48f + 0.12f * g, 0.16f);
        mb.material = S3Mat.Leaves;
        mb.Icosphere(new Vector3(0, trunkH + 0.9f * s, 0), new Vector3(1.35f, 1.15f, 1.35f) * s, 2, leaf, r, 0.05f, TopLit(leaf), lumps: 0.25f);
        mb.Icosphere(new Vector3(0.45f * s, trunkH + 1.7f * s, 0.2f * s), new Vector3(0.85f, 0.8f, 0.85f) * s, 2, Mul(leaf, 1.08f), r, 0.05f, TopLit(Mul(leaf, 1.08f)), lumps: 0.25f);
        mb.Icosphere(new Vector3(-0.5f * s, trunkH + 0.5f * s, -0.3f * s), new Vector3(0.8f, 0.7f, 0.8f) * s, 2, Mul(leaf, 0.92f), r, 0.05f, TopLit(Mul(leaf, 0.92f)), lumps: 0.25f);
        if (r.NextDouble() < 0.35)   // apples
        {
            mb.material = S3Mat.Paint;
            for (int k = 0; k < 7; k++)
            {
                float a = 2 * Mathf.PI * (float)r.NextDouble(), y = (float)r.NextDouble() * 0.9f - 0.2f;
                var dir = new Vector3(Mathf.Cos(a), y, Mathf.Sin(a)).normalized;
                Vector3 at = new Vector3(0, trunkH + 0.9f * s, 0) + Vector3.Scale(dir, new Vector3(1.32f, 1.12f, 1.32f) * s);
                mb.Icosphere(at, Vector3.one * 0.09f * s, 0, new Color(0.86f, 0.14f, 0.1f));
            }
        }
        mb.material = S3Mat.Plain;
    }


    /// <summary>A fir: four tiers of needles on a slim trunk.</summary>
    static void PineTree(MeshBuilder mb, System.Random rng, float s)
    {
        var r = Detail(rng, 1);
        mb.material = S3Mat.Bark;
        mb.Frustum(Vector3.zero, 0.2f * s, 0.12f * s, 1.3f * s, 8, Bark);
        float g = (float)r.NextDouble();
        var leaf = new Color(0.12f + 0.05f * g, 0.38f + 0.1f * g, 0.20f);
        mb.material = S3Mat.Needles;
        for (int k = 0; k < 4; k++)
        {
            float y = (0.85f + 0.78f * k) * s, rad = (1.55f - 0.3f * k) * s;
            mb.Frustum(new Vector3(0, y, 0), rad, 0, (1.55f - 0.08f * k) * s, 9, Mul(leaf, 0.92f + 0.07f * k), k * 0.35f + 0.2f * (float)r.NextDouble());
        }
        mb.material = S3Mat.Plain;
    }


    static void Tuft(MeshBuilder mb, System.Random rng)
    {
        mb.material = S3Mat.Grass;
        int blades = 3 + rng.Next(3);
        float g = (float)rng.NextDouble();
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            Vector3 b = dir * 0.05f * (float)rng.NextDouble();
            float h = 0.2f + 0.25f * (float)rng.NextDouble();
            Vector3 tip = b + dir * (0.08f + 0.1f * (float)rng.NextDouble()) + Vector3.up * h;
            var col = new Color(0.25f + 0.1f * g, 0.55f + 0.12f * g, 0.18f);
            Blade(mb, b, tip, 0.035f, col);
        }
        mb.material = S3Mat.Plain;
    }

    /// <summary>A curved blade from base to tip: a tapering quad, bending over into a pointed tip.</summary>
    static void Blade(MeshBuilder mb, Vector3 b, Vector3 tip, float halfWidth, Color col)
    {
        Vector3 lean = tip - b;
        lean.y = 0;
        var side = lean.sqrMagnitude > 1e-6f ? new Vector3(-lean.z, 0, lean.x).normalized : Vector3.right;
        Vector3 mid = b + new Vector3(lean.x * 0.3f, (tip.y - b.y) * 0.62f, lean.z * 0.3f);
        mb.Quad(b - side * halfWidth, b + side * halfWidth, mid + side * halfWidth * 0.75f, mid - side * halfWidth * 0.75f, col);
        mb.Tri(mid - side * halfWidth * 0.75f, mid + side * halfWidth * 0.75f, tip, col);
    }

    /// <summary>A toadstool at o, scale s: a pale stem, a gill skirt and a spotted red cap.</summary>
    static void Toadstool(MeshBuilder mb, Vector3 o, float s)
    {
        mb.material = S3Mat.Plain;
        mb.Frustum(o, 0.055f * s, 0.04f * s, 0.22f * s, 8, new Color(0.95f, 0.92f, 0.85f));
        mb.Frustum(o + new Vector3(0, 0.19f * s, 0), 0.16f * s, 0.05f * s, 0.03f * s, 10, new Color(0.88f, 0.80f, 0.70f));
        mb.material = S3Mat.Mushroom;
        mb.Icosphere(o + new Vector3(0, 0.21f * s, 0), new Vector3(0.18f, 0.11f, 0.18f) * s, 1, new Color(0.88f, 0.12f, 0.1f), smooth: true,
                     faceColor: d => d.y < -0.1f ? new Color(0.88f, 0.80f, 0.70f) : new Color(0.88f, 0.12f, 0.1f));
        mb.material = S3Mat.Plain;
    }

    /// <summary>A round bush of leafy lobes, sometimes with berries.</summary>
    static void Bush(MeshBuilder mb, System.Random rng, float s, bool berries)
    {
        mb.material = S3Mat.Leaves;
        float g = (float)rng.NextDouble();
        var leaf = new Color(0.18f + 0.08f * g, 0.44f + 0.12f * g, 0.17f);
        int lobes = 3 + rng.Next(3);
        for (int k = 0; k < lobes; k++)
        {
            float a = 2 * Mathf.PI * k / lobes + (float)rng.NextDouble();
            float rad = (0.42f + 0.2f * (float)rng.NextDouble()) * s;
            var at = new Vector3(Mathf.Cos(a) * 0.35f * s, rad * 0.75f, Mathf.Sin(a) * 0.35f * s);
            mb.Icosphere(at, new Vector3(rad, rad * 0.85f, rad), 1, leaf, rng, 0.06f, TopLit(leaf), lumps: 0.2f);
        }
        if (!berries) { mb.material = S3Mat.Plain; return; }
        mb.material = S3Mat.Paint;
        var berry = rng.NextDouble() < 0.5 ? new Color(0.78f, 0.1f, 0.18f) : new Color(0.32f, 0.22f, 0.62f);
        for (int k = 0; k < 14; k++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble(), y = (float)rng.NextDouble() * 0.6f;
            var d = new Vector3(Mathf.Cos(a), y, Mathf.Sin(a)).normalized;
            mb.Icosphere(new Vector3(0, 0.38f * s, 0) + d * (0.6f * s), Vector3.one * 0.04f * s, 0, berry);
        }
        mb.material = S3Mat.Plain;
    }

    /// <summary>A wildflower: a stem, a couple of leaves and a head of petals around a centre.</summary>
    static void Flower(MeshBuilder mb, System.Random rng, Color petal)
    {
        float h = 0.18f + 0.22f * (float)rng.NextDouble();
        float lean = (float)rng.NextDouble() * 0.06f, a = 2 * Mathf.PI * (float)rng.NextDouble();
        var top = new Vector3(Mathf.Cos(a) * lean, h, Mathf.Sin(a) * lean);
        var stem = new Color(0.28f, 0.55f, 0.2f);
        mb.material = S3Mat.Grass;
        mb.Tube(Vector3.zero, top, 0.008f, 0.006f, 4, stem);
        Blade(mb, Vector3.zero, new Vector3(Mathf.Cos(a + 2), 0.12f, Mathf.Sin(a + 2)) * 0.6f, 0.02f, stem);
        mb.material = S3Mat.Petal;
        int n = 5 + rng.Next(3);
        float r = 0.035f + 0.025f * (float)rng.NextDouble();
        for (int k = 0; k < n; k++)
        {
            float t = 2 * Mathf.PI * k / n;
            var d = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
            var sd = new Vector3(-d.z, 0, d.x);
            mb.Tri(top + sd * r * 0.28f, top + d * r + Vector3.up * 0.012f, top - sd * r * 0.28f, petal);
            mb.Tri(top + d * r + Vector3.up * 0.012f, top + d * r * 0.55f + sd * r * 0.38f, top + sd * r * 0.28f, petal);
            mb.Tri(top - sd * r * 0.28f, top + d * r * 0.55f - sd * r * 0.38f, top + d * r + Vector3.up * 0.012f, petal);
        }
        mb.material = S3Mat.Plain;
        mb.Icosphere(top + Vector3.up * 0.012f, new Vector3(r * 0.32f, r * 0.2f, r * 0.32f), 0, new Color(1.0f, 0.78f, 0.15f));
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
        mb.material = S3Mat.Paving;
        mb.Box(new Vector3(-5, 0, -3), new Vector3(5, P, 5), Paving);
        mb.material = S3Mat.Brick;
        mb.Box(new Vector3(-5.15f, P, -3.15f), new Vector3(5.15f, B, 5.15f), Brick);

        // stairs down to the front, with stepped terracotta side walls and pillars
        const int steps = 6;
        for (int k = 0; k < steps; k++)
        {
            float y = B * (steps - k) / steps, z1 = -3.15f - 0.5f * k, z0 = z1 - 0.5f;
            mb.material = S3Mat.Paving;
            mb.Box(new Vector3(-1.3f, 0, z0), new Vector3(1.3f, y, z1), Paving);
            mb.material = S3Mat.Brick;
            mb.Box(new Vector3(-1.65f, 0, z0), new Vector3(-1.3f, y + 0.3f, z1), Terracotta);
            mb.Box(new Vector3(1.3f, 0, z0), new Vector3(1.65f, y + 0.3f, z1), Terracotta);
        }
        mb.Box(new Vector3(-1.75f, B, -3.4f), new Vector3(-1.2f, B + 0.75f, -2.85f), Terracotta);
        mb.Box(new Vector3(1.2f, B, -3.4f), new Vector3(1.75f, B + 0.75f, -2.85f), Terracotta);
        Lantern(mb, new Vector3(-1.475f, B + 0.75f, -3.125f));
        Lantern(mb, new Vector3(1.475f, B + 0.75f, -3.125f));
        Planter(mb, new Vector3(-4.0f, B, -2.2f));
        Planter(mb, new Vector3(4.0f, B, -2.2f));

        // flagstone path leading to the stairs
        for (int i = 0; i < 7; i++)
        {
            float z = -6.6f - 0.75f * i, x = (i % 2 == 0 ? -0.45f : 0.4f) + 0.15f * Mathf.Sin(i * 2.3f);
            var c = Color.Lerp(Paving, Sand, 0.3f + 0.1f * (i % 3));
            mb.material = S3Mat.Paving;
            mb.Box(new Vector3(x - 0.4f, 0, z - 0.3f), new Vector3(x + 0.4f, 0.05f, z + 0.3f), c);
            mb.Box(new Vector3(-x - 0.3f, 0, z - 0.6f), new Vector3(-x + 0.25f, 0.05f, z - 0.15f), c);
        }

        // main block, tower and side wing
        mb.material = S3Mat.Adobe;
        mb.Box(new Vector3(-3.6f, B, 0), new Vector3(1.6f, B + 3.3f, 4.2f), Adobe);
        mb.Box(new Vector3(-1.0f, B, 0.6f), new Vector3(2.0f, B + 7.6f, 3.6f), Adobe);
        mb.Box(new Vector3(1.6f, B, 0.8f), new Vector3(4.4f, B + 2.5f, 4.2f), AdobeShade);
        // cornices
        mb.Box(new Vector3(-3.75f, B + 3.3f, -0.15f), new Vector3(1.75f, B + 3.5f, 4.35f), Trim);
        mb.Box(new Vector3(-1.15f, B + 7.6f, 0.45f), new Vector3(2.15f, B + 7.85f, 3.75f), Trim);
        mb.Box(new Vector3(1.45f, B + 2.5f, 0.65f), new Vector3(4.55f, B + 2.68f, 4.35f), Trim);
        mb.Box(new Vector3(-1.0f, B + 5.2f, 0.5f), new Vector3(2.0f, B + 5.35f, 0.6f), Trim);   // band on the tower
        mb.material = S3Mat.Brick;
        Crenels(mb, -1.1f, 2.1f, 0.5f, 3.7f, B + 7.85f, 0.32f, 0.45f, Brick);
        Crenels(mb, -3.7f, -1.05f, -0.1f, 4.3f, B + 3.5f, 0.28f, 0.35f, Brick);

        // trim beam, awning and draped cloth
        mb.material = S3Mat.Paint;
        mb.Box(new Vector3(-3.6f, B + 2.05f, -0.12f), new Vector3(1.6f, B + 2.2f, 0), TrimGreen);
        mb.Box(new Vector3(-3.5f, B + 2.5f, -1.05f), new Vector3(-0.3f, B + 2.6f, -0.95f), TrimGreen);
        mb.material = S3Mat.Cloth;
        mb.Quad(new Vector3(-3.4f, B + 2.95f, 0), new Vector3(-0.4f, B + 2.95f, 0),
                new Vector3(-0.4f, B + 2.5f, -1.0f), new Vector3(-3.4f, B + 2.5f, -1.0f), Cloth);
        for (int i = 0; i < 6; i++)   // scalloped edge
        {
            float x0 = -3.4f + 0.5f * i, x1 = x0 + 0.5f;
            mb.Tri(new Vector3(x0, B + 2.5f, -1.0f), new Vector3(x1, B + 2.5f, -1.0f), new Vector3((x0 + x1) / 2, B + 2.25f, -1.02f), Cloth);
        }
        mb.Quad(new Vector3(-1.0f, B + 7.5f, 0.7f), new Vector3(-1.0f, B + 7.5f, 2.6f),
                new Vector3(-3.3f, B + 3.5f, 3.4f), new Vector3(-3.3f, B + 3.5f, 0.3f), Cloth);

        // a pennant on the tower
        mb.material = S3Mat.Wood;
        mb.Tube(new Vector3(0.5f, B + 7.85f, 2.1f), new Vector3(0.5f, B + 9.6f, 2.1f), 0.035f, 0.025f, 6, Wood);
        mb.material = S3Mat.Cloth;
        mb.Tri(new Vector3(0.5f, B + 9.55f, 2.1f), new Vector3(0.5f, B + 9.0f, 2.1f), new Vector3(0.5f, B + 9.3f, 3.05f), Cloth);

        // vigas (roof beams poking out of the facade)
        mb.material = S3Mat.Wood;
        for (float x = -3.3f; x < -1.1f; x += 0.55f)
            mb.Box(new Vector3(x, B + 3.0f, -0.35f), new Vector3(x + 0.14f, B + 3.14f, 0), Wood);

        // openings
        Vector3 right = Vector3.right, front = Vector3.back;
        mb.material = S3Mat.Wood;
        mb.Arch(new Vector3(0.4f, B, 0), right, front, 1.2f, 2.3f, Wood);              // door
        mb.material = S3Mat.Plain;
        mb.Arch(new Vector3(-2.4f, B, 0), right, front, 1.6f, 2.0f, Gate);             // barred gate
        mb.material = S3Mat.Metal;
        for (float x = -3.0f; x < -1.7f; x += 0.3f)
            mb.Box(new Vector3(x, B, -0.07f), new Vector3(x + 0.05f, B + 1.75f, -0.03f), Opening);
        mb.material = S3Mat.Plain;
        mb.Arch(new Vector3(0.5f, B + 5.9f, 0.6f), right, front, 0.7f, 1.2f, Opening);  // tower windows
        mb.Arch(new Vector3(-0.3f, B + 3.9f, 0.6f), right, front, 0.4f, 0.7f, Opening);
        mb.Arch(new Vector3(1.3f, B + 3.9f, 0.6f), right, front, 0.4f, 0.7f, Opening);
        mb.Arch(new Vector3(2.0f, B + 4.8f, 2.1f), Vector3.forward, Vector3.right, 0.55f, 0.95f, Opening);
        mb.Arch(new Vector3(3.0f, B + 0.6f, 0.8f), right, front, 0.7f, 1.2f, Opening); // wing
        foreach (float x in new[] { -3.0f, -2.2f })
            mb.Quad(new Vector3(x - 0.2f, B + 2.75f, -0.03f), new Vector3(x + 0.2f, B + 2.75f, -0.03f),
                    new Vector3(x + 0.2f, B + 3.2f, -0.03f), new Vector3(x - 0.2f, B + 3.2f, -0.03f), Opening);
        mb.material = S3Mat.Plain;
    }

    /// <summary>A lamp on a post top: an iron frame around a warm glowing glass.</summary>
    static void Lantern(MeshBuilder mb, Vector3 at)
    {
        mb.material = S3Mat.Metal;
        mb.Box(at + new Vector3(-0.12f, 0, -0.12f), at + new Vector3(0.12f, 0.05f, 0.12f), Gate);
        mb.Frustum(at + Vector3.up * 0.33f, 0.16f, 0, 0.14f, 4, Gate, Mathf.PI / 4);
        mb.material = S3Mat.Glass;
        mb.Box(at + new Vector3(-0.09f, 0.05f, -0.09f), at + new Vector3(0.09f, 0.33f, 0.09f), new Color(1f, 0.78f, 0.38f, 0.35f));
        mb.material = S3Mat.Plain;
    }


    /// <summary>Square terracotta planter with a leafy shrub.</summary>
    static void Planter(MeshBuilder mb, Vector3 at)
    {
        mb.material = S3Mat.Brick;
        mb.Frustum(at, 0.5f, 0.62f, 1.1f, 4, Terracotta, Mathf.PI / 4);
        mb.material = S3Mat.Paint;
        mb.Frustum(at + Vector3.up * 1.1f, 0.68f, 0.68f, 0.12f, 4, Trim, Mathf.PI / 4);
        mb.material = S3Mat.Leaves;
        var leaf = new Color(0.38f, 0.55f, 0.2f);
        mb.Icosphere(at + Vector3.up * 1.45f, new Vector3(0.48f, 0.4f, 0.48f), 2, leaf, null, 0, TopLit(leaf));
        mb.material = S3Mat.Plain;
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
        mb.material = S3Mat.Adobe;
        mb.Box(new Vector3(-x, 0, -z), new Vector3(x, h, z), Adobe);
        mb.Box(new Vector3(-x - 0.12f, h, -z - 0.12f), new Vector3(x + 0.12f, h + 0.18f, z + 0.12f), Trim);
        mb.material = S3Mat.Brick;
        Crenels(mb, -x, x, -z, z, h + 0.18f, 0.28f, 0.35f, Brick);
        mb.material = S3Mat.Wood;
        mb.Arch(new Vector3(-x * 0.3f, 0, -z), Vector3.right, Vector3.back, 0.9f, 1.9f, Wood);
        mb.material = S3Mat.Plain;
        mb.Arch(new Vector3(x * 0.45f, 1.2f, -z), Vector3.right, Vector3.back, 0.55f, 0.9f, Opening);
        mb.Arch(new Vector3(x, 1.2f, 0), Vector3.forward, Vector3.right, 0.55f, 0.9f, Opening);
        mb.Arch(new Vector3(-x, 1.2f, 0), Vector3.back, Vector3.left, 0.55f, 0.9f, Opening);
        mb.material = S3Mat.Wood;
        for (float vx = -x + 0.3f; vx < x - 0.2f; vx += 0.6f)
            mb.Box(new Vector3(vx, h - 0.45f, -z - 0.3f), new Vector3(vx + 0.12f, h - 0.33f, -z), Wood);
        if (rng.NextDouble() < 0.6)   // a strip of awning
        {
            mb.material = S3Mat.Cloth;
            mb.Quad(new Vector3(-x + 0.2f, h - 0.6f, -z), new Vector3(x - 0.2f, h - 0.6f, -z),
                    new Vector3(x - 0.2f, h - 1.0f, -z - 0.8f), new Vector3(-x + 0.2f, h - 1.0f, -z - 0.8f),
                    rng.NextDouble() < 0.5 ? Cloth : TrimGreen);
        }
        mb.material = S3Mat.Plain;
    }


    /// <summary>Curved, ringed trunk with a crown of drooping, folded fronds with leaflets. Returns the crown height.</summary>
    static float Palm(MeshBuilder mb, System.Random rng, float s)
    {
        float H = (5.0f + 2.2f * (float)rng.NextDouble()) * s;
        float leanAngle = 2 * Mathf.PI * (float)rng.NextDouble(), lean = (0.6f + 1.2f * (float)rng.NextDouble()) * s;
        var leanDir = new Vector3(Mathf.Cos(leanAngle), 0, Mathf.Sin(leanAngle));
        var barkA = new Color(0.56f, 0.43f, 0.31f);
        var barkB = new Color(0.50f, 0.38f, 0.27f);
        const int seg = 10;
        Vector3 prev = Vector3.zero;
        float rPrev = 0.29f * s;
        mb.material = S3Mat.PalmBark;
        for (int k = 1; k <= seg; k++)
        {
            float t = (float)k / seg;
            Vector3 p = leanDir * (lean * t * t) + Vector3.up * (H * t);
            float r = Mathf.Lerp(0.27f, 0.16f, t) * s;
            mb.Tube(prev, p, rPrev, r, 8, k % 2 == 0 ? barkA : barkB);
            prev = p; rPrev = r;
        }
        Vector3 crown = prev;
        int fronds = 8 + rng.Next(3);
        float g = (float)rng.NextDouble();
        var dark = new Color(0.20f + 0.05f * g, 0.42f + 0.08f * g, 0.16f);
        var light = new Color(0.42f, 0.60f, 0.20f);
        mb.material = S3Mat.Frond;
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
                mb.Quad(fold0, c0 + side * w0, c1 + side * w1, fold1, Mul(col, 0.92f));
                // leaflet tips fringing both edges
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 a0 = c0 + side * (e * w0), a1 = c1 + side * (e * w1);
                    Vector3 tip = Vector3.Lerp(a0, a1, 0.7f) + side * (e * 0.14f * s) + Vector3.down * (0.08f * s) + dir * (0.05f * s);
                    mb.Tri(a0, a1, tip, Mul(col, e < 0 ? 1f : 0.92f));
                }
            }
        }
        mb.material = S3Mat.Plain;
        for (int i = 0; i < 3; i++)   // coconuts
        {
            float a = 2 * Mathf.PI * i / 3;
            mb.Icosphere(crown + new Vector3(Mathf.Cos(a) * 0.18f, -0.25f, Mathf.Sin(a) * 0.18f) * s, Vector3.one * 0.13f * s, 1, new Color(0.40f, 0.30f, 0.16f), smooth: true);
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
            mb.material = i % 2 == 0 ? S3Mat.Adobe : S3Mat.Brick;
            mb.Box(new Vector3(x, 0, -0.22f), new Vector3(x + w, h, 0.22f), col);
            x += w;
        }
        // a fallen block
        mb.material = S3Mat.Adobe;
        mb.Box(new Vector3(-0.3f, 0, 0.6f), new Vector3(0.35f, 0.3f, 1.1f), AdobeShade);
        mb.material = S3Mat.Plain;
    }


    static void TallGrass(MeshBuilder mb, System.Random rng)
    {
        mb.material = S3Mat.Grass;
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
            mb.Quad(b - side * 0.04f, b + side * 0.04f, mid + side * 0.03f, mid - side * 0.03f, Mul(col, 0.85f));
            mb.Tri(mid - side * 0.03f, mid + side * 0.03f, tip, col);
        }
        mb.material = S3Mat.Plain;
    }


    static void DryTuft(MeshBuilder mb, System.Random rng)
    {
        mb.material = S3Mat.Grass;
        int blades = 4 + rng.Next(4);
        float g = (float)rng.NextDouble();
        var col = new Color(0.74f + 0.1f * g, 0.64f + 0.08f * g, 0.38f);
        for (int i = 0; i < blades; i++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble();
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float h = 0.2f + 0.25f * (float)rng.NextDouble();
            Vector3 tip = dir * (0.12f + 0.12f * (float)rng.NextDouble()) + Vector3.up * h;
            Blade(mb, Vector3.zero, tip, 0.03f, col);
        }
        mb.material = S3Mat.Plain;
    }

    /// <summary>A saguaro: a ribbed column with one or two arms. Returns its height.</summary>
    static float Cactus(MeshBuilder mb, System.Random rng)
    {
        mb.material = S3Mat.Cactus;
        var col = new Color(0.36f, 0.56f, 0.32f);
        float h = 1.6f + 1.4f * (float)rng.NextDouble(), r = 0.2f + 0.06f * (float)rng.NextDouble();
        mb.Tube(Vector3.zero, Vector3.up * h, r, r * 0.95f, 10, col);
        mb.Icosphere(Vector3.up * h, Vector3.one * r * 0.95f, 2, col, smooth: true);
        int arms = 1 + rng.Next(2);
        for (int k = 0; k < arms; k++)
        {
            float a = 2 * Mathf.PI * (float)rng.NextDouble(), y = h * (0.35f + 0.25f * (float)rng.NextDouble());
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float ar = r * 0.7f, reach = 0.45f + 0.2f * (float)rng.NextDouble(), rise = 0.6f + 0.6f * (float)rng.NextDouble();
            Vector3 elbow = Vector3.up * y + d * reach;
            mb.Tube(Vector3.up * y, elbow, ar, ar, 8, col);
            mb.Icosphere(elbow, Vector3.one * ar, 1, col, smooth: true);
            mb.Tube(elbow, elbow + Vector3.up * rise, ar, ar * 0.95f, 8, col);
            mb.Icosphere(elbow + Vector3.up * rise, Vector3.one * ar * 0.95f, 1, col, smooth: true);
        }
        // a flower or two on top
        mb.material = S3Mat.Petal;
        mb.Icosphere(Vector3.up * (h + r * 0.85f), new Vector3(0.07f, 0.05f, 0.07f), 1, new Color(0.98f, 0.55f, 0.65f));
        mb.material = S3Mat.Plain;
        return h + 0.2f;
    }

    /// <summary>A few clay pots and jars, some tipped over.</summary>
    static void Jars(MeshBuilder mb, System.Random rng)
    {
        int n = 1 + rng.Next(3);
        for (int k = 0; k < n; k++)
        {
            var at = new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.6f;
            float s = 0.25f + 0.2f * (float)rng.NextDouble();
            var clay = Color.Lerp(Terracotta, new Color(0.82f, 0.62f, 0.44f), (float)rng.NextDouble());
            mb.material = S3Mat.Paint;
            mb.Icosphere(at + Vector3.up * s, new Vector3(s * 0.8f, s, s * 0.8f), 2, clay, smooth: true,
                         faceColor: d => Mathf.Abs(d.y) < 0.12f ? Mul(clay, 0.8f) : clay);
            mb.Frustum(at + Vector3.up * s * 1.8f, s * 0.3f, s * 0.38f, s * 0.3f, 10, Mul(clay, 0.92f));
        }
        mb.material = S3Mat.Plain;
    }

    // ------------------------------------------------------------------ golf props (local meshes)


    public static Mesh FlagMesh()
    {
        var mb = new MeshBuilder(0.3f) { material = S3Mat.Paint };
        mb.Tube(Vector3.zero, new Vector3(0, 2.4f, 0), 0.025f, 0.022f, 8, new Color(0.95f, 0.95f, 0.92f));
        // a gently waving pennant
        mb.material = S3Mat.Cloth;
        var red = new Color(0.95f, 0.15f, 0.12f);
        const int n = 6;
        for (int i = 0; i < n; i++)
        {
            float u0 = (float)i / n, u1 = (float)(i + 1) / n;
            Vector3 P(float u, float v) => new Vector3(0, Mathf.Lerp(2.38f, 1.82f, v) + (2.1f - Mathf.Lerp(2.38f, 1.82f, v)) * u, 0.07f * Mathf.Sin(u * 5f)) + new Vector3(0.8f * u, 0, 0);
            mb.Quad(P(u0, 0), P(u1, 0), P(u1, 1), P(u0, 1), red);
        }
        mb.material = S3Mat.Paint;
        mb.Icosphere(new Vector3(0, 2.43f, 0), Vector3.one * 0.05f, 1, new Color(0.95f, 0.8f, 0.2f), smooth: true);
        return mb.ToMesh("S3 Flag");
    }


    public static Mesh BallMesh(float r)
    {
        var mb = new MeshBuilder(1f) { material = S3Mat.Ball };
        var red = new Color(0.9f, 0.15f, 0.1f);
        mb.Icosphere(Vector3.zero, Vector3.one * r, 3, Color.white, null, 0,
                     c => Mathf.Abs(c.y) < 0.2f || (c.x > 0.78f) ? red : new Color(0.97f, 0.97f, 0.95f), smooth: true);
        return mb.ToMesh("S3 Ball");
    }


    /// <summary>Launch guide: glowing dots along local +z from just past the ball, yellow to orange.</summary>
    public static Mesh GuideMesh(float ballRadius)
    {
        var mb = new MeshBuilder(1f) { material = S3Mat.Paint };
        const int n = 9;
        for (int k = 0; k < n; k++)
        {
            var col = Color.Lerp(new Color(1f, 0.95f, 0.3f, 0.6f), new Color(1f, 0.42f, 0.08f, 0.6f), k / (n - 1f));
            mb.Icosphere(new Vector3(0, 0, ballRadius + 0.2f + 0.32f * k), Vector3.one * (0.06f - 0.003f * k), 1, col, smooth: true);
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
