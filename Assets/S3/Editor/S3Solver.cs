using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Block = S3Laser.Block;
using Kind = S3Laser.Kind;

/// <summary>
/// Searches for the ways a laser puzzle can be solved, to check a lock only gives way to its intended
/// trick. Mirrors that matter stand where the light already is, so the first piece is sampled along the
/// lit ground beams. A second mirror has to send light into a goal, so it stands where some lit beam crosses
/// a "feed": a ray traced backwards out of a goal (bouncing off fixed mirrors), from any point of which light
/// sent the other way arrives in that goal. Two great circles cross in exactly two antipodal points, so these
/// are found exactly instead of by sampling.
/// </summary>
public static class S3Solver
{
    const float Tau = 2 * Mathf.PI;

    public struct Feed { public Vector4 start, dir; public float length; }

    public static bool Solves(List<Block> blocks, IEnumerable<Block> pieces, List<Block> obstacles)
    {
        var scene = new List<Block>(blocks);
        scene.AddRange(pieces);
        return new S3Laser.Simulation(scene, obstacles).LightsAllGoals(scene);
    }

    public static int LitCount(List<Block> blocks, IEnumerable<Block> pieces, List<Block> obstacles)
    {
        var scene = new List<Block>(blocks);
        scene.AddRange(pieces);
        return new S3Laser.Simulation(scene, obstacles).litGoals.Count;
    }

    /// <summary>Could a player put this piece down here (like S3Puzzle's hologram check)?</summary>
    public static bool CanPlace(Block piece, List<Block> blocks, List<Block> obstacles, Func<Vector4, bool> forbidden, float R)
    {
        float margin = 0.05f / R;
        foreach (var b in blocks) if (piece.FootprintOverlaps(b, margin)) return false;
        if (obstacles != null) foreach (var b in obstacles) if (piece.FootprintOverlaps(b, margin)) return false;
        return forbidden == null || !forbidden(piece.position);
    }

    static bool OnGround(S3Laser.Segment s) => Mathf.Abs(s.start.y) < 1e-4f && Mathf.Abs(s.dir.y) < 1e-4f;
    static Vector4 Flat(Vector4 v, Vector4 p) => S3.Ortho(v, p, S3.EY);

    static void TraceBack(Vector4 x, Vector4 d, float remaining, List<Block> scene, List<Block> obstacles, Block skip, float R, List<Feed> feeds)
    {
        for (int bounce = 0; bounce <= 6; bounce++)
        {
            float bestT = remaining;
            Block hit = null;
            S3Laser.Hit best = default;
            foreach (var b in scene) if (b != skip && b.Intersect(x, d, out var h) && h.t < bestT) { bestT = h.t; hit = b; best = h; }
            if (obstacles != null) foreach (var b in obstacles) if (b.Intersect(x, d, out var h) && h.t < bestT) { bestT = h.t; hit = b; best = h; }
            if (bestT * R > 0.4f) feeds.Add(new Feed { start = x, dir = d, length = bestT });
            if (hit == null || !(hit.kind == Kind.Mirror && best.axis == 0)) return;
            Vector4 p = S3Laser.PointAt(x, d, bestT), hd = S3Laser.TangentAt(x, d, bestT);
            x = p.normalized;
            d = S3Laser.Reflect(hd, best.normal);
            d = (d - Vector4.Dot(d, x) * x).normalized;
            remaining -= bestT;
            skip = null;
        }
    }

    /// <summary>Backward rays out of every goal, through its centre and four offsets across its width.</summary>
    public static List<Feed> Feeds(List<Block> scene, List<Block> obstacles, float R, float stepDegrees = 0.5f)
    {
        var feeds = new List<Feed>();
        float[] offsets = { -0.36f, -0.18f, 0, 0.18f, 0.36f };
        foreach (var g in scene)
        {
            if (g.kind != Kind.Goal) continue;
            for (float a = 0; a < 360; a += stepDegrees)
            {
                Vector4 arrive = S3Laser.Turn(g.facing, g.position, a * Mathf.Deg2Rad);
                Vector4 side = S3Laser.Right(g.position, arrive);
                foreach (float off in offsets)
                {
                    Vector4 q = Mathf.Abs(off) < 1e-6f ? g.position : S3.Walk(g.position, side, off / R);
                    TraceBack(q, -Flat(S3Laser.Transport(arrive, g.position, q), q), Tau, scene, obstacles, g, R, feeds);
                }
            }
        }
        return feeds;
    }

    static bool Cross(Vector4 s0, Vector4 d0, float l0, Vector4 s1, Vector4 d1, float l1, int which, out Vector4 x, out float t0, out float t1)
    {
        var a = new Vector3(s0.x, s0.z, s0.w); var b = new Vector3(d0.x, d0.z, d0.w);
        var c = new Vector3(s1.x, s1.z, s1.w); var d = new Vector3(d1.x, d1.z, d1.w);
        Vector3 v = Vector3.Cross(Vector3.Cross(a, b), Vector3.Cross(c, d));
        x = default; t0 = t1 = 0;
        if (v.sqrMagnitude < 1e-10f) return false;
        v = v.normalized * (which == 0 ? 1 : -1);
        t0 = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(v, b), Vector3.Dot(v, a)), Tau);
        t1 = Mathf.Repeat(Mathf.Atan2(Vector3.Dot(v, d), Vector3.Dot(v, c)), Tau);
        const float eps = 0.02f;
        if (!(t0 > eps && t0 < l0 - eps && t1 > eps && t1 < l1 - eps)) return false;
        x = new Vector4(v.x, 0, v.y, v.z);
        return true;
    }

    /// <summary>Mirrors where a lit ground beam crosses a feed, turned to send the beam down it.</summary>
    static IEnumerable<Block> CrossMirrors(S3Laser.Segment s, Feed f, float R)
    {
        float l0 = Mathf.Min(s.length, Tau);
        for (int which = 0; which < 2; which++)
        {
            if (!Cross(s.start, s.dir, l0, f.start, f.dir, f.length, which, out var x, out var t0, out var t1)) continue;
            Vector4 a = Flat(S3Laser.TangentAt(s.start, s.dir, t0), x);
            Vector4 b = -Flat(S3Laser.TangentAt(f.start, f.dir, t1), x);
            if ((a - b).magnitude < 1e-3f) continue;
            yield return Block.Mirror(x, (a - b).normalized, R);
        }
    }

    /// <summary>Every single mirror that lights all goals, found exactly at beam/feed crossings.</summary>
    public static List<Block> OneMirror(List<Block> blocks, List<Block> obstacles, Func<Vector4, bool> forbidden, float R)
    {
        var feeds = Feeds(blocks, obstacles, R);
        var found = new List<Block>();
        foreach (var s in new S3Laser.Simulation(blocks, obstacles).segments)
        {
            if (s.diffracted || !OnGround(s)) continue;
            foreach (var f in feeds)
                foreach (var m in CrossMirrors(s, f, R))
                {
                    if (found.Exists(o => S3.Dist(o.position, m.position) * R < 0.25f && Mathf.Abs(Vector4.Dot(o.facing, m.facing)) > 0.995f)) continue;
                    if (CanPlace(m, blocks, obstacles, forbidden, R) && Solves(blocks, new[] { m }, obstacles)) found.Add(m);
                }
        }
        return found;
    }

    /// <summary>
    /// Mirrors sampled every spacing metres along the lit ground beams and turned in steps of stepDegrees that
    /// light all goals (one per spot). Slower than OneMirror but also sees what light reaches through diffractors.
    /// </summary>
    public static List<Block> OneMirrorSampled(List<Block> blocks, List<Block> obstacles, Func<Vector4, bool> forbidden, float R,
                                               float spacing = 0.2f, float stepDegrees = 0.5f)
    {
        var found = new List<Block>();
        foreach (var s in new S3Laser.Simulation(blocks, obstacles).segments)
        {
            if (s.diffracted || !OnGround(s)) continue;
            float len = Mathf.Min(s.length, Tau);
            int n = Mathf.CeilToInt(len * R / spacing);
            for (int k = 1; k < n; k++)
            {
                float t = len * k / n;
                Vector4 p = S3.Foot(S3Laser.PointAt(s.start, s.dir, t));
                Vector4 dir = Flat(S3Laser.TangentAt(s.start, s.dir, t), p);
                for (float a = 0; a < 180; a += stepDegrees)
                {
                    var m = Block.Mirror(p, S3Laser.Turn(dir, p, a * Mathf.Deg2Rad), R);
                    if (!CanPlace(m, blocks, obstacles, forbidden, R)) continue;
                    if (Solves(blocks, new[] { m }, obstacles)) { found.Add(m); break; }
                }
            }
        }
        return found;
    }

    /// <summary>Pairs of mirrors that light all goals: the first sampled on the lit beams, the second where any beam then crosses a feed.</summary>
    public static List<(Block first, Block second)> TwoMirrors(List<Block> blocks, List<Block> obstacles, Func<Vector4, bool> forbidden, float R,
                                                                float spacing = 0.25f, float stepDegrees = 2f, int max = 5000)
    {
        var feeds = Feeds(blocks, obstacles, R);
        var found = new List<(Block, Block)>();
        foreach (var s in new S3Laser.Simulation(blocks, obstacles).segments)
        {
            if (s.diffracted || !OnGround(s)) continue;
            float len = Mathf.Min(s.length, Tau);
            int n = Mathf.CeilToInt(len * R / spacing);
            for (int k = 1; k < n; k++)
            {
                float t = len * k / n;
                Vector4 p = S3.Foot(S3Laser.PointAt(s.start, s.dir, t));
                Vector4 dir = Flat(S3Laser.TangentAt(s.start, s.dir, t), p);
                for (float a = stepDegrees / 2; a < 180; a += stepDegrees)
                {
                    var m1 = Block.Mirror(p, S3Laser.Turn(dir, p, a * Mathf.Deg2Rad), R);
                    if (!CanPlace(m1, blocks, obstacles, forbidden, R)) continue;
                    var scene = new List<Block>(blocks) { m1 };
                    var sim = new S3Laser.Simulation(scene, obstacles);
                    if (sim.LightsAllGoals(scene)) { found.Add((m1, null)); continue; }
                    bool done = false;
                    foreach (var s1 in sim.segments)
                    {
                        if (done) break;
                        if (s1.diffracted || !OnGround(s1)) continue;
                        foreach (var f in feeds)
                        {
                            foreach (var m2 in CrossMirrors(s1, f, R))
                                if (CanPlace(m2, scene, obstacles, forbidden, R) && Solves(scene, new[] { m2 }, obstacles))
                                {
                                    found.Add((m1, m2));
                                    if (found.Count >= max) return found;
                                    done = true;
                                    break;
                                }
                            if (done) break;
                        }
                    }
                }
            }
        }
        return found;
    }

    /// <summary>
    /// Pairs of mirrors that light all goals by bringing the beam to target (a diffractor's spot, which takes light
    /// from any side): the first sampled on the lit beams, the second sampled along its new light and aimed at target
    /// either way round. Only arrivals within maxAngle degrees of a multiple of 30° to heading reference are tried.
    /// </summary>
    public static List<(Block first, Block second)> TwoMirrorsInto(List<Block> blocks, List<Block> obstacles, Func<Vector4, bool> forbidden, float R,
                                                                    Vector4 target, Vector4 reference, float maxAngle = 14f, int max = 2000)
    {
        var found = new List<(Block, Block)>();
        foreach (var s in new S3Laser.Simulation(blocks, obstacles).segments)
        {
            if (s.diffracted || !OnGround(s)) continue;
            float len = Mathf.Min(s.length, Tau);
            int n = Mathf.CeilToInt(len * R / 0.3f);
            for (int k = 1; k < n; k++)
            {
                float t = len * k / n;
                Vector4 p = S3.Foot(S3Laser.PointAt(s.start, s.dir, t));
                Vector4 dir = Flat(S3Laser.TangentAt(s.start, s.dir, t), p);
                for (float a = 1; a < 180; a += 3f)
                {
                    var m1 = Block.Mirror(p, S3Laser.Turn(dir, p, a * Mathf.Deg2Rad), R);
                    if (!CanPlace(m1, blocks, obstacles, forbidden, R)) continue;
                    var scene = new List<Block>(blocks) { m1 };
                    foreach (var s1 in new S3Laser.Simulation(scene, obstacles).segments)
                    {
                        if (s1.diffracted || !OnGround(s1) || s1.start == s.start) continue;
                        float len1 = Mathf.Min(s1.length, Tau);
                        int n1 = Mathf.CeilToInt(len1 * R / 0.2f);
                        for (int k1 = 1; k1 < n1; k1++)
                        {
                            float t1 = len1 * k1 / n1;
                            Vector4 q = S3.Foot(S3Laser.PointAt(s1.start, s1.dir, t1));
                            Vector4 incoming = Flat(S3Laser.TangentAt(s1.start, s1.dir, t1), q);
                            Vector4 toTarget = S3.DirTo(q, target);
                            if (toTarget == Vector4.zero) continue;
                            for (int way = 0; way < 2; way++)
                            {
                                Vector4 outgoing = way == 0 ? toTarget : -toTarget;
                                float dist = way == 0 ? S3.Dist(q, target) : Tau - S3.Dist(q, target);
                                Vector4 arrive = Flat(S3Laser.TangentAt(q, outgoing, dist), target);
                                float angle = Mathf.Atan2(Vector4.Dot(arrive, S3Laser.Right(target, reference)), Vector4.Dot(arrive, reference)) * Mathf.Rad2Deg;
                                if (Mathf.Abs(Mathf.Repeat(angle + 15, 30) - 15) > maxAngle || (incoming - outgoing).magnitude < 1e-3f) continue;
                                var m2 = Block.Mirror(q, (incoming - outgoing).normalized, R);
                                if (!CanPlace(m2, scene, obstacles, forbidden, R) || !Solves(scene, new[] { m2 }, obstacles)) continue;
                                found.Add((m1, m2));
                                if (found.Count >= max) return found;
                            }
                        }
                    }
                }
            }
        }
        return found;
    }

    /// <summary>"1.0-2.4, 3.1, 5.0-6.2": sorted values merged into runs no more than join apart.</summary>
    public static string Runs(IEnumerable<float> values, float join = 0.35f)
    {
        var v = values.OrderBy(x => x).ToList();
        if (v.Count == 0) return "none";
        var parts = new List<string>();
        float a = v[0], b = v[0];
        foreach (var x in v.Skip(1))
        {
            if (x - b <= join) { b = x; continue; }
            parts.Add(a == b ? $"{a:0.0}" : $"{a:0.0}-{b:0.0}");
            a = b = x;
        }
        parts.Add(a == b ? $"{a:0.0}" : $"{a:0.0}-{b:0.0}");
        return string.Join(", ", parts);
    }
}
