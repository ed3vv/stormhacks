using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly props (houses, trees, rocks, mushrooms, grass) in the style of Hyperbolica.
/// Each prop is authored in plain local metres, placed on the ground great-sphere by a frame,
/// and baked into one shared scenery mesh.
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

    struct KeepOut { public Vector4 p; public float r; public KeepOut(Vector4 p, float r) { this.p = p; this.r = r; } }

    public static MeshBuilder Build(S3World world, System.Random rng)
    {
        float R = world.radius;
        var scene = new MeshBuilder();
        var keep = new List<KeepOut>();
        foreach (var h in world.holes)
        {
            keep.Add(new KeepOut(h.tee, 3f));
            keep.Add(new KeepOut(h.cup, world.greenRadius + 1f));
            // keep a fairway clear along the geodesic from tee to cup
            float len = S3.Dist(h.tee, h.cup);
            Vector4 dir = S3.DirTo(h.tee, h.cup);
            if (dir == Vector4.zero) continue;
            for (float s = 0; s < len * R; s += 1.5f)
                keep.Add(new KeepOut(S3.Walk(h.tee, dir, s / R), 1.4f));
        }

        // ---------------------------------------------------------------- houses (fixed spots)
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
            scene.AppendBaked(mb, frame, R);
            Vector4 foot = frame.GetColumn(3);
            float footprint = 0.5f * Mathf.Sqrt(w * w + d * d);
            world.obstacles.Add(new S3World.Obstacle(foot, footprint * 0.9f, h + rh));
            keep.Add(new KeepOut(foot, footprint + 1.5f));
        }

        // ---------------------------------------------------------------- trees
        for (int i = 0; i < 34; i++)
        {
            if (!TryPlace(rng, R, keep, 1.8f, out var frame)) continue;
            var mb = new MeshBuilder();
            float scale = 0.8f + 0.5f * (float)rng.NextDouble();
            if (rng.NextDouble() < 0.65) RoundTree(mb, rng, scale); else PineTree(mb, rng, scale);
            scene.AppendBaked(mb, frame, R);
            Vector4 foot = frame.GetColumn(3);
            world.obstacles.Add(new S3World.Obstacle(foot, 0.3f * scale, 6f));
            keep.Add(new KeepOut(foot, 1.6f * scale));
        }

        // ---------------------------------------------------------------- rocks
        for (int i = 0; i < 22; i++)
        {
            float size = 0.35f + 1.1f * (float)(rng.NextDouble() * rng.NextDouble());
            if (!TryPlace(rng, R, keep, size + 0.5f, out var frame)) continue;
            var mb = new MeshBuilder(0.6f);
            float g = 0.72f + 0.12f * (float)rng.NextDouble();
            mb.Icosphere(new Vector3(0, size * 0.35f, 0), new Vector3(size, size * 0.7f, size * 0.9f), 1,
                         new Color(g, g, g * 0.98f), rng, 0.18f);
            scene.AppendBaked(mb, frame, R);
            Vector4 foot = frame.GetColumn(3);
            world.obstacles.Add(new S3World.Obstacle(foot, size * 0.85f, size));
            keep.Add(new KeepOut(foot, size + 0.3f));
        }

        // ---------------------------------------------------------------- mushrooms
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
            scene.AppendBaked(mb, frame, R);
        }

        // ---------------------------------------------------------------- grass tufts (all over the world)
        for (int i = 0; i < 1100; i++)
        {
            if (!TryPlace(rng, R, keep, 0.2f, out var frame, attempts: 4)) continue;
            var mb = new MeshBuilder(1f);
            Tuft(mb, rng);
            scene.AppendBaked(mb, frame, R);
        }
        return scene;
    }

    static bool TryPlace(System.Random rng, float R, List<KeepOut> keep, float radius, out Matrix4x4 frame, int attempts = 40)
    {
        for (int a = 0; a < attempts; a++)
        {
            // uniform on the ground sphere: cos(distance) is uniform
            float dist = Mathf.Acos(1 - 2 * (float)rng.NextDouble()) * R;
            float theta = 2 * Mathf.PI * (float)rng.NextDouble();
            frame = S3.GroundFrame(dist * Mathf.Cos(theta), dist * Mathf.Sin(theta), 2 * Mathf.PI * (float)rng.NextDouble(), R);
            Vector4 foot = frame.GetColumn(3);
            bool ok = true;
            foreach (var k in keep)
                if (S3.Dist(foot, k.p) * R < k.r + radius) { ok = false; break; }
            if (ok) return true;
        }
        frame = Matrix4x4.identity;
        return false;
    }

    // ------------------------------------------------------------------ props

    /// <summary>Gabled house: walls w (x) by d (z), wall height h, roof rise rh. Front faces -z.</summary>
    static void House(MeshBuilder mb, float w, float d, float h, float rh)
    {
        float x = w / 2, z = d / 2;
        // walls
        mb.Quad(new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, h, -z), new Vector3(-x, h, -z), Wall);
        mb.Quad(new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, h, z), new Vector3(x, h, z), Wall);
        mb.Quad(new Vector3(-x, 0, z), new Vector3(-x, 0, -z), new Vector3(-x, h, -z), new Vector3(-x, h, z), WallEdge);
        mb.Quad(new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(x, h, z), new Vector3(x, h, -z), WallEdge);
        // gables
        mb.Tri(new Vector3(-x, h, -z), new Vector3(x, h, -z), new Vector3(0, h + rh, -z), Wall);
        mb.Tri(new Vector3(x, h, z), new Vector3(-x, h, z), new Vector3(0, h + rh, z), Wall);
        // roof: two thick slabs whose planes meet the wall tops exactly
        float o = 0.45f, eave = h - rh * o / x, zo = z + o;
        mb.Slab(new Vector3(-x - o, eave, -zo), new Vector3(-x - o, eave, zo), new Vector3(0, h + rh, zo), new Vector3(0, h + rh, -zo), 0.2f, Roof, RoofEdge);
        mb.Slab(new Vector3(x + o, eave, zo), new Vector3(x + o, eave, -zo), new Vector3(0, h + rh, -zo), new Vector3(0, h + rh, zo), 0.2f, Roof, RoofEdge);
        // chimney
        mb.Box(new Vector3(x * 0.35f, h + rh * 0.4f, z * 0.3f), new Vector3(x * 0.35f + 0.5f, h + rh + 0.7f, z * 0.3f + 0.5f), Chimney);

        // windows and door, slightly proud of the walls
        void Window(Vector3 c, Vector3 outward, Vector3 side, float ww, float wh)
        {
            Vector3 up = Vector3.up;
            void Rect(float hw, float hh, float off, Color col)
            {
                Vector3 cc = c + outward * off;
                mb.Quad(cc - side * hw - up * hh, cc + side * hw - up * hh, cc + side * hw + up * hh, cc - side * hw + up * hh, col);
            }
            Rect(ww / 2 + 0.08f, wh / 2 + 0.08f, 0.02f, Frame);
            Rect(ww / 2, wh / 2, 0.04f, Pane);
            mb.Quad(c + outward * 0.05f - side * 0.03f - up * wh / 2, c + outward * 0.05f + side * 0.03f - up * wh / 2,
                    c + outward * 0.05f + side * 0.03f + up * wh / 2, c + outward * 0.05f - side * 0.03f + up * wh / 2, Frame);
            mb.Quad(c + outward * 0.05f - side * ww / 2 - up * 0.03f, c + outward * 0.05f + side * ww / 2 - up * 0.03f,
                    c + outward * 0.05f + side * ww / 2 + up * 0.03f, c + outward * 0.05f - side * ww / 2 + up * 0.03f, Frame);
        }
        float wy = Mathf.Min(1.65f, h - 0.8f);
        // front: door + window
        Vector3 doorC = new Vector3(-x * 0.45f, 1.0f, -z);
        mb.Quad(doorC + new Vector3(-0.5f, -1.0f, -0.02f), doorC + new Vector3(0.5f, -1.0f, -0.02f),
                doorC + new Vector3(0.5f, 1.05f, -0.02f), doorC + new Vector3(-0.5f, 1.05f, -0.02f), Frame);
        mb.Quad(doorC + new Vector3(-0.42f, -1.0f, -0.04f), doorC + new Vector3(0.42f, -1.0f, -0.04f),
                doorC + new Vector3(0.42f, 0.97f, -0.04f), doorC + new Vector3(-0.42f, 0.97f, -0.04f), Door);
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
    public static Mesh GuideMesh()
    {
        var mb = new MeshBuilder(1f);
        const int n = 9;
        for (int k = 0; k < n; k++)
        {
            var col = Color.Lerp(new Color(1f, 0.95f, 0.3f), new Color(1f, 0.42f, 0.08f), k / (n - 1f));
            mb.Icosphere(new Vector3(0, 0, 0.24f + 0.2f * k), Vector3.one * (0.032f - 0.0015f * k), 1, col);
        }
        return mb.ToMesh("S3 Guide");
    }

    /// <summary>Flat aiming arrow lying on the ground along +z, starting just past the ball.</summary>
    public static Mesh ArrowMesh()
    {
        var mb = new MeshBuilder(0.3f);
        var col = new Color(1f, 0.95f, 0.55f);
        float y = 0.015f;
        mb.Quad(new Vector3(-0.04f, y, 0.25f), new Vector3(0.04f, y, 0.25f), new Vector3(0.04f, y, 1.45f), new Vector3(-0.04f, y, 1.45f), col);
        mb.Tri(new Vector3(-0.14f, y, 1.45f), new Vector3(0.14f, y, 1.45f), new Vector3(0, y, 1.75f), col);
        return mb.ToMesh("S3 Arrow");
    }
}
