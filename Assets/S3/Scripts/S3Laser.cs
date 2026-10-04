using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Laser light on S^3, ported from the JS laser maze on the "laser" branch
/// (src/game/laser/geometry.js and simulation.js) into this project's conventions:
/// the ground is the great 2-sphere y = 0 and e_y is up.
///
/// The ground is totally geodesic, so a laser sent along it stays on it: beams are great circles of
/// the ground sphere. They go all the way round the world (2 pi R) and come back from behind, and
/// every ray leaving a point meets again at its antipode, pi R away.
///
/// Blocks are boxes standing on the ground, defined in the gnomonic chart centred on their foot.
/// Great circles are straight lines in that chart, so a ray-box test is the ordinary slab test.
/// </summary>
public static class S3Laser
{
    public const float Tau = 2 * Mathf.PI;
    /// <summary>How far blocks reach below the ground (radians), so beams in the ground strike them cleanly.</summary>
    public const float Sink = 0.02f;

    public enum Kind { Wall, Emitter, Goal, Mirror, Diffractor, Scenery }

    public static Vector4 PointAt(Vector4 x, Vector4 d, float t) => Mathf.Cos(t) * x + Mathf.Sin(t) * d;
    public static Vector4 TangentAt(Vector4 x, Vector4 d, float t) => Mathf.Cos(t) * d - Mathf.Sin(t) * x;
    public static Vector4 Reflect(Vector4 d, Vector4 n) => d - 2 * Vector4.Dot(d, n) * n;

    /// <summary>Distance along x(t) = x cos t + d sin t until it first passes below the ground; infinity if it lies in the ground.</summary>
    public static float GroundHit(Vector4 x, Vector4 d)
    {
        float a = x.y, b = d.y;
        if (Mathf.Abs(a) < 1e-7f && Mathf.Abs(b) < 1e-7f) return float.PositiveInfinity;
        if (a <= 1e-7f && b < 0) return 0;
        // y(t) = A cos(t - phi) crosses zero going down at phi + pi/2
        float t = Mathf.Atan2(b, a) + Mathf.PI / 2;
        while (t <= 1e-5f) t += Tau;
        while (t > Tau + 1e-5f) t -= Tau;
        return t;
    }

    /// <summary>Unit horizontal tangent at ground point p, a quarter turn right of heading f.</summary>
    public static Vector4 Right(Vector4 p, Vector4 f) => S3.FrameAt(p, S3.EY, f).GetColumn(0);

    /// <summary>Heading f at ground point p turned right by angle radians.</summary>
    public static Vector4 Turn(Vector4 f, Vector4 p, float angle) =>
        (Mathf.Cos(angle) * f + Mathf.Sin(angle) * Right(p, f)).normalized;

    /// <summary>Parallel transport of tangent w from a to b along their geodesic.</summary>
    public static Vector4 Transport(Vector4 w, Vector4 a, Vector4 b) =>
        w - (a + b) * (Vector4.Dot(w, b) / (1 + Vector4.Dot(a, b)));

    public struct Hit { public float t; public int axis; public Vector4 normal; }

    /// <summary>
    /// A box standing on the ground. Chart axes are 0 = facing, 1 = side, 2 = up; lo and hi are
    /// chart bounds (tangents of angles). A mirror reflects from its two axis-0 faces.
    /// </summary>
    public class Block
    {
        public readonly Kind kind;
        public readonly Vector4 position, facing, side;
        public readonly Vector3 lo, hi;
        /// <summary>Metres: half length along facing, half width along side, height. Used to build its mesh.</summary>
        public readonly Vector3 size;
        public readonly bool movable;
        /// <summary>Part of a sealed cup's barrier; removed when the cup unlocks.</summary>
        public bool gate;
        readonly float boundingCos;

        public Block(Kind kind, Vector4 position, Vector4 facing, Vector3 size, float R, bool movable = false)
        {
            this.kind = kind;
            this.position = S3.Foot(position);
            Vector4 f = S3.Ortho(facing, this.position, S3.EY);
            if (f == Vector4.zero) f = S3.Ortho(S3.EZ, this.position, S3.EY);
            if (f == Vector4.zero) f = S3.Ortho(S3.EX, this.position, S3.EY);
            this.facing = f;
            side = Right(this.position, f);
            this.size = size;
            this.movable = movable;
            hi = new Vector3(Mathf.Tan(size.x / R), Mathf.Tan(size.y / R), Mathf.Tan(size.z / R));
            lo = new Vector3(-hi.x, -hi.y, -Mathf.Tan(Sink));
            float corner = new Vector3(hi.x, hi.y, Mathf.Max(hi.z, -lo.z)).magnitude;
            boundingCos = Mathf.Cos(Mathf.Atan(corner)) * 0.999f;
        }

        // Sizes in metres: (half length along facing, half width, height).
        public static readonly Vector3 EmitterSize = new Vector3(0.42f, 0.34f, 0.75f);
        public static readonly Vector3 GoalSize = new Vector3(0.5f, 0.5f, 1.25f);
        public static readonly Vector3 MirrorSize = new Vector3(0.06f, 0.8f, 1.3f);
        public static readonly Vector3 DiffractorSize = new Vector3(0.34f, 0.34f, 0.62f);
        public const float WallThickness = 0.22f, WallHeight = 0.9f;

        public static Block Emitter(Vector4 p, Vector4 f, float R) => new Block(Kind.Emitter, p, f, EmitterSize, R);
        public static Block Goal(Vector4 p, Vector4 f, float R) => new Block(Kind.Goal, p, f, GoalSize, R);
        public static Block Mirror(Vector4 p, Vector4 f, float R, bool movable = true) => new Block(Kind.Mirror, p, f, MirrorSize, R, movable);
        public static Block Diffractor(Vector4 p, Vector4 f, float R, bool movable = true) => new Block(Kind.Diffractor, p, f, DiffractorSize, R, movable);

        /// <summary>A straight wall along the geodesic from a to b.</summary>
        public static Block Wall(Vector4 a, Vector4 b, float R, float thickness = WallThickness, float height = WallHeight)
        {
            Vector4 middle = (a + b).normalized;
            return new Block(Kind.Wall, middle, S3.DirTo(middle, b),
                new Vector3(S3.Dist(a, b) * R / 2 + thickness / 2, thickness / 2, height), R);
        }

        /// <summary>A world prop (tree trunk, rock, house) as a box that simply absorbs light.</summary>
        public static Block Scenery(S3World.Obstacle o, float R) =>
            new Block(Kind.Scenery, o.foot, S3.Ortho(S3.EZ, o.foot, S3.EY), new Vector3(o.radius * 0.85f, o.radius * 0.85f, Mathf.Max(o.height, 0.3f)), R);

        public Block Rotated(float angle, float R) => new Block(kind, position, Turn(facing, position, angle), size, R, movable) { gate = gate };
        public Block MovedTo(Vector4 p, Vector4 f, float R) => new Block(kind, p, f, size, R, movable) { gate = gate };

        /// <summary>The frame meshes for this block are drawn in: local x = side, y = up, z = facing.</summary>
        public Matrix4x4 Frame => S3.FrameAt(position, S3.EY, facing);

        /// <summary>First entry of the geodesic x(t) = x cos t + d sin t into the box.</summary>
        public bool Intersect(Vector4 x, Vector4 d, out Hit hit)
        {
            hit = default;
            Vector4 c = position;
            float p3 = Vector4.Dot(x, c), d3 = Vector4.Dot(d, c);
            if (p3 * p3 + d3 * d3 < boundingCos * boundingCos) return false;   // never comes close
            // Re-parametrise from the point nearest the centre. On the hemisphere around the centre the
            // chart point moves along a straight line, linear in tan(t - tc), so the slab test applies.
            float tc = Mathf.Atan2(d3, p3), ct = Mathf.Cos(tc), st = Mathf.Sin(tc);
            float qw = p3 * ct + d3 * st;
            float enter = float.NegativeInfinity, exit = float.PositiveInfinity;
            int axis = 0;
            float dir0 = 0, dir1 = 0, dir2 = 0;
            if (!Slab(Vector4.Dot(x, facing), Vector4.Dot(d, facing), 0, ref dir0)) return false;
            if (!Slab(Vector4.Dot(x, side), Vector4.Dot(d, side), 1, ref dir1)) return false;
            if (!Slab(x.y, d.y, 2, ref dir2)) return false;
            if (!(enter <= exit) || float.IsNegativeInfinity(enter)) return false;
            float t = tc + Mathf.Atan(enter);
            if (t <= 1e-4f) t += Tau;
            float da = axis == 0 ? dir0 : axis == 1 ? dir1 : dir2;
            bool positive = da < 0;
            float bound = positive ? hi[axis] : lo[axis];
            Vector4 basis = axis == 0 ? facing : axis == 1 ? side : S3.EY;
            hit = new Hit { t = t, axis = axis, normal = (basis - c * bound).normalized * (positive ? 1 : -1) };
            return true;

            bool Slab(float pl, float dl, int i, ref float dir)
            {
                float o = (pl * ct + dl * st) / qw;
                dir = (dl * ct - pl * st) / qw;
                if (Mathf.Abs(dir) < 1e-12f) return o >= lo[i] && o <= hi[i];
                float a = (lo[i] - o) / dir, b = (hi[i] - o) / dir;
                if (Mathf.Min(a, b) > enter) { enter = Mathf.Min(a, b); axis = i; }
                exit = Mathf.Min(exit, Mathf.Max(a, b));
                return true;
            }
        }

        /// <summary>Is ground point p within the footprint, grown by margin (radians)?</summary>
        public bool FootprintContains(Vector4 p, float margin)
        {
            float cp = Vector4.Dot(p, position);
            if (cp <= 0) return false;
            return Mathf.Abs(Vector4.Dot(p, facing) / cp) <= hi.x + margin
                && Mathf.Abs(Vector4.Dot(p, side) / cp) <= hi.y + margin;
        }

        Vector4 Corner(float a, float b) => (position + facing * a + side * b).normalized;

        /// <summary>Separating-axis test between the two footprints, in this block's chart.</summary>
        public bool FootprintOverlaps(Block other, float margin)
        {
            float reach = Mathf.Atan(new Vector2(hi.x, hi.y).magnitude) + Mathf.Atan(new Vector2(other.hi.x, other.hi.y).magnitude);
            if (S3.Dist(position, other.position) > reach + margin) return false;
            float a = hi.x + margin, b = hi.y + margin;
            var mine = new[] { new Vector2(a, b), new Vector2(a, -b), new Vector2(-a, -b), new Vector2(-a, b) };
            float oa = other.hi.x, ob = other.hi.y;
            var theirs = new Vector2[4];
            var oc = new[] { other.Corner(oa, ob), other.Corner(oa, -ob), other.Corner(-oa, -ob), other.Corner(-oa, ob) };
            for (int i = 0; i < 4; i++)
            {
                float cp = Vector4.Dot(oc[i], position);
                if (cp <= 1e-4f) return true;   // degenerate: far round the world, treat as touching
                theirs[i] = new Vector2(Vector4.Dot(oc[i], facing) / cp, Vector4.Dot(oc[i], side) / cp);
            }
            var axes = new[] { Vector2.right, Vector2.up, Perp(theirs[1] - theirs[0]), Perp(theirs[2] - theirs[1]) };
            foreach (var ax in axes)
            {
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    float pa = Vector2.Dot(mine[i], ax), pb = Vector2.Dot(theirs[i], ax);
                    minA = Mathf.Min(minA, pa); maxA = Mathf.Max(maxA, pa);
                    minB = Mathf.Min(minB, pb); maxB = Mathf.Max(maxB, pb);
                }
                if (maxA < minB || maxB < minA) return false;
            }
            return true;
        }

        static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

        /// <summary>
        /// Pushes a circle (a ball or the player, radius in radians) out of the footprint. Returns the
        /// outward horizontal normal at the block and how far (radians) the circle overlaps.
        /// </summary>
        public bool PushCircle(Vector4 foot, float radius, out Vector4 normal, out float depth)
        {
            normal = Vector4.zero; depth = 0;
            float cp = Vector4.Dot(foot, position);
            if (cp < boundingCos * 0.95f) return false;
            float u = Vector4.Dot(foot, facing) / cp, v = Vector4.Dot(foot, side) / cp;
            float cu = Mathf.Clamp(u, -hi.x, hi.x), cv = Mathf.Clamp(v, -hi.y, hi.y);
            float du = u - cu, dv = v - cv, dist = Mathf.Sqrt(du * du + dv * dv);
            if (dist >= radius) return false;
            if (dist < 1e-7f)
            {
                // centre inside: leave by the nearest face
                float px = hi.x - Mathf.Abs(u), py = hi.y - Mathf.Abs(v);
                if (px < py) { du = Mathf.Sign(u); dv = 0; depth = px + radius; }
                else { du = 0; dv = Mathf.Sign(v); depth = py + radius; }
            }
            else { du /= dist; dv /= dist; depth = radius - dist; }
            normal = (facing * du + side * dv).normalized;
            return true;
        }
    }

    public struct Segment
    {
        public Vector4 start, dir;
        public float length;
        public bool diffracted, preview;
    }

    /// <summary>
    /// All the laser light in a scene and what it reaches. blocks are the puzzle's own blocks (indices
    /// in litGoals refer to them); obstacles are the world's scenery, which absorbs light. With a
    /// hologram index, only light that has been reflected or re-emitted by that block is kept.
    /// </summary>
    public class Simulation
    {
        /// <summary>Elevation (radians) and number of rays in each ring a diffractor emits.</summary>
        static readonly Vector2[] Rings = { new Vector2(0, 12), new Vector2(0.33f, 12), new Vector2(0.72f, 10), new Vector2(1.13f, 8) };

        public readonly List<Segment> segments = new List<Segment>();
        public readonly HashSet<int> litGoals = new HashSet<int>();
        public readonly HashSet<int> activeDiffractors = new HashSet<int>();
        readonly List<Block> blocks, obstacles;
        readonly int hologram;

        struct DiffractorHit { public int index; public Vector4 incoming; public bool via; }

        public Simulation() { blocks = obstacles = new List<Block>(); hologram = -1; }

        public Simulation(List<Block> blocks, List<Block> obstacles, int hologram = -1)
        {
            this.blocks = blocks;
            this.obstacles = obstacles ?? new List<Block>();
            this.hologram = hologram;
            var hits = new List<DiffractorHit>();
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].kind == Kind.Emitter)
                    Trace(blocks[i].position, blocks[i].facing, 2 * Tau, 24, false, false, hits);
            foreach (var h in hits)
            {
                if (!activeDiffractors.Add(h.index)) continue;
                bool onlyViaHologram = h.index == hologram || hits.TrueForAll(o => o.index != h.index || o.via);
                Diffract(blocks[h.index], h.incoming, onlyViaHologram);
            }
        }

        int Count => blocks.Count + obstacles.Count;
        Block Get(int i) => i < blocks.Count ? blocks[i] : obstacles[i - blocks.Count];

        public bool LightsAllGoals(List<Block> scene)
        {
            bool any = false;
            for (int i = 0; i < scene.Count; i++)
                if (scene[i].kind == Kind.Goal)
                {
                    any = true;
                    if (!litGoals.Contains(i)) return false;
                }
            return any;
        }

        /// <summary>Re-emits light from the diffractor's core in every upward direction. It all meets again at the antipode, pi away.</summary>
        void Diffract(Block block, Vector4 incoming, bool viaHologram)
        {
            Vector4 g = block.position;
            Vector4 a = S3.Ortho(incoming, g, S3.EY);
            if (a == Vector4.zero) a = block.facing;
            Vector4 b = Right(g, a);
            Trace(g, S3.EY, Mathf.PI + 0.1f, 6, true, viaHologram, null);
            for (int ring = 0; ring < Rings.Length; ring++)
            {
                float elevation = Rings[ring].x;
                int count = (int)Rings[ring].y;
                for (int k = 0; k < count; k++)
                {
                    float az = Tau * (k + (ring % 2 == 1 ? 0.5f : 0)) / count;
                    Vector4 d = Mathf.Cos(elevation) * (Mathf.Cos(az) * a + Mathf.Sin(az) * b) + Mathf.Sin(elevation) * S3.EY;
                    Trace(g, d, Mathf.PI + 0.1f, 6, true, viaHologram, null);
                }
            }
        }

        void Trace(Vector4 x, Vector4 d, float remaining, int bounces, bool diffracted, bool via, List<DiffractorHit> hits)
        {
            for (int bounce = 0; bounce <= bounces; bounce++)
            {
                float bestT = remaining;
                int hitIndex = -1;
                Hit best = default;
                for (int i = 0; i < Count; i++)
                    if (Get(i).Intersect(x, d, out var h) && h.t < bestT) { bestT = h.t; hitIndex = i; best = h; }
                float g = GroundHit(x, d);
                if (g < bestT) { bestT = g; hitIndex = -1; }
                if (bestT > 1e-6f && (hologram < 0 || via))
                    segments.Add(new Segment { start = x, dir = d, length = bestT, diffracted = diffracted, preview = hologram >= 0 });
                if (hitIndex < 0) return;
                Vector4 p = PointAt(x, d, bestT), hd = TangentAt(x, d, bestT);
                Kind kind = Get(hitIndex).kind;
                if (kind == Kind.Mirror && best.axis == 0)
                {
                    x = p.normalized;
                    d = Reflect(hd, best.normal);
                    d = (d - Vector4.Dot(d, x) * x).normalized;
                    remaining -= bestT;
                    via |= hitIndex == hologram;
                }
                else
                {
                    if (kind == Kind.Goal) litGoals.Add(hitIndex);
                    else if (kind == Kind.Diffractor && !diffracted && hits != null)
                        hits.Add(new DiffractorHit { index = hitIndex, incoming = hd, via = via });
                    return;
                }
            }
        }
    }
}
