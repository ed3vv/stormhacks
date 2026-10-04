using System.Collections.Generic;
using UnityEngine;
using Block = S3Laser.Block;

/// <summary>
/// The laser puzzles, adapted from the "laser" branch (src/game/laser/levels.js). Each one is a
/// template laid out around a frame: compass north is the frame's forward, east its right, and
/// spots are given as (north, east) in radians of arc, exactly as in the JS. The Desert plays them
/// as stand-alone puzzles; the Eternal Poles have their own set (frozen mirrors, a prism, twin
/// lasers, the antipode) as the locks on its cups.
/// </summary>
public static class S3Levels
{
    public class Level
    {
        public string title, hint;
        public List<Block> blocks = new List<Block>();
        public int mirrors, diffractors;
        /// <summary>A placement of the pieces that lights every goal (used for tests and to keep scenery out of the way).</summary>
        public List<Block> solution = new List<Block>();
        public Vector4 start, startFacing;

        public Block Goal => blocks.Find(b => b.kind == S3Laser.Kind.Goal);
    }

    /// <summary>The first five are the Desert's; the rest are the Eternal Poles' own locks (new ones go at the end: the scene stores these as numbers).</summary>
    public enum Template { Reflection, LongWay, Antipode, Zigzag, FullCircle, FrozenMirror, Prism, TwinBeams, Homecoming, FrozenRelay }

    public static readonly Template[] DesertOrder =
        { Template.Reflection, Template.LongWay, Template.Antipode, Template.Zigzag, Template.FullCircle };

    /// <summary>Lays out one puzzle around frame (columns: east, up, north, origin). mirrorX flips east and west.</summary>
    public static Level Build(Template template, Matrix4x4 frame, float R, bool mirrorX = false)
    {
        Vector4 origin = frame.GetColumn(3), north = frame.GetColumn(2), east = frame.GetColumn(0) * (mirrorX ? -1 : 1);

        Vector4 Spot(float n, float e)
        {
            Vector4 v = north * n + east * e;
            float len = v.magnitude;
            return len < 1e-7f ? origin : S3.Walk(origin, v / len, len);
        }
        // compass heading in degrees (0 north, 90 east) at p, carrying the origin's directions over
        Vector4 Heading(float degrees, Vector4 p)
        {
            float a = degrees * Mathf.Deg2Rad;
            Vector4 w = north * Mathf.Cos(a) + east * Mathf.Sin(a);
            return S3.Ortho(S3Laser.Transport(w, origin, p), p, S3.EY);
        }
        // mirror at m, struck by a beam straight from source, sending it toward target (or directly away from it)
        Block Mirror(Vector4 m, Vector4 source, Vector4 target, bool away = false)
        {
            Vector4 incoming = -S3.DirTo(m, source), outgoing = S3.DirTo(m, target) * (away ? -1 : 1);
            return Block.Mirror(m, (incoming - outgoing).normalized, R);
        }
        // the point of the great circle through p with tangent f that is nearest to q
        Vector4 NearestOnCircle(Vector4 p, Vector4 f, Vector4 q)
        {
            float t = Mathf.Atan2(Vector4.Dot(q, f), Vector4.Dot(q, p));
            return S3.Walk(p, f, t);
        }
        // walls along sides of a square around c (0 north, 1 east, 2 south, 3 west)
        IEnumerable<Block> Enclosure(Vector4 c, float r, params int[] sides)
        {
            Vector4 f = Heading(0, c), e = S3Laser.Right(c, f) * (mirrorX ? -1 : 1);
            Vector4 Corner(float a, float b)
            {
                Vector4 v = f * (a * r) + e * (b * r);
                return S3.Walk(c, v.normalized, v.magnitude);
            }
            Vector4 ne = Corner(1, 1), se = Corner(-1, 1), sw = Corner(-1, -1), nw = Corner(1, -1);
            var edges = new[] { (nw, ne), (ne, se), (se, sw), (sw, nw) };
            foreach (int s in sides) yield return Block.Wall(edges[s].Item1, edges[s].Item2, R);
        }
        // a point n north and e east of c, in c's own compass directions
        Vector4 Offset(Vector4 c, float n, float e)
        {
            Vector4 f = Heading(0, c), r = S3Laser.Right(c, f) * (mirrorX ? -1 : 1);
            Vector4 v = f * n + r * e;
            float len = v.magnitude;
            return len < 1e-7f ? c : S3.Walk(c, v / len, len);
        }
        // a frozen mirror: part of the level, can't be picked up or turned
        Block FixedMirror(Vector4 m, Vector4 source, Vector4 target, float halfWidth)
        {
            Vector4 incoming = -S3.DirTo(m, source), outgoing = S3.DirTo(m, target);
            return new Block(S3Laser.Kind.Mirror, m, (incoming - outgoing).normalized,
                             new Vector3(Block.MirrorSize.x, halfWidth, Block.MirrorSize.z), R, movable: false);
        }
        // the side of an enclosure (0 north, 1 east, 2 south, 3 west) with a gap of half-width gap in its middle
        IEnumerable<Block> Gap(Vector4 c, float r, int side, float gap)
        {
            float sn = side == 0 ? 1 : side == 2 ? -1 : 0, se = side == 1 ? 1 : side == 3 ? -1 : 0;
            Vector4 P(float along) => Offset(c, sn * r + se * along, se * r + sn * along);
            yield return Block.Wall(P(-r), P(-gap), R);
            yield return Block.Wall(P(gap), P(r), R);
        }
        // two walls running out from that gap to distance far from c, so only light along the channel gets in
        IEnumerable<Block> Channel(Vector4 c, float r, float far, int side, float gap)
        {
            float sn = side == 0 ? 1 : side == 2 ? -1 : 0, se = side == 1 ? 1 : side == 3 ? -1 : 0;
            Vector4 P(float out_, float along) => Offset(c, sn * out_ + se * along, se * out_ + sn * along);
            yield return Block.Wall(P(r, -gap), P(far, -gap), R);
            yield return Block.Wall(P(r, gap), P(far, gap), R);
        }
        Level New(string title, string hint) => new Level { title = title, hint = hint, start = origin, startFacing = north };

        switch (template)
        {
            case Template.Reflection:
            {
                Vector4 e = Spot(.35f, -.3f), g = Spot(.8f, .25f), ef = Heading(90, e);
                Vector4 m = NearestOnCircle(e, ef, Spot(.35f, .25f));
                var level = New("Reflection",
                    "Guide the red laser into the crystal. Look at the ground and click to place a mirror: " +
                    "its hologram shows where the beam would go. Hold Q / E to turn it. " +
                    "Click a placed mirror to pick it up again.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.mirrors = 1;
                level.solution.Add(Mirror(m, e, g));
                return level;
            }
            case Template.LongWay:
            {
                Vector4 e = Spot(.1f, -.25f), g = Spot(.45f, .3f), ef = Heading(0, e);
                Vector4 m = NearestOnCircle(e, ef, Spot(.45f, -.25f));
                var level = New("The Long Way Round",
                    "This side of the crystal is walled off. But the world is a closed sphere: " +
                    "a beam sent the other way goes all the way round (75 m) and comes back from behind.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.AddRange(Enclosure(g, .075f, 0, 2, 3));
                level.mirrors = 1;
                level.solution.Add(Mirror(m, e, g, true));
                return level;
            }
            case Template.Antipode:
            {
                Vector4 g = Spot(.42f, 0), e = Spot(.42f, -.45f);
                var level = New("Antipode",
                    "The crystal is sealed in. In this curved space every ray leaving a point meets again at " +
                    "its antipode, 37.7 m away in every direction. Put the diffractor on the beam exactly " +
                    "37.7 m from the crystal: from there, the crystal hangs straight overhead.");
                level.blocks.Add(Block.Emitter(e, -S3.DirTo(e, g), R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.AddRange(Enclosure(g, .085f, 0, 1, 2, 3));
                level.diffractors = 1;
                level.solution.Add(Block.Diffractor(-g, Heading(0, -g), R));
                return level;
            }
            case Template.Zigzag:
            {
                Vector4 e = Spot(.05f, -.35f), g = Spot(.62f, .35f), ef = Heading(0, e);
                Vector4 m1 = NearestOnCircle(e, ef, Spot(.25f, -.35f)), m2 = Spot(.25f, .35f);
                var level = New("Zigzag", "Two mirrors. The crystal only accepts light from the south.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.Add(Block.Wall(Spot(.5f, -.46f), Spot(.5f, -.24f), R));
                level.blocks.Add(Block.Wall(Spot(.36f, 0), Spot(.85f, 0), R));
                level.blocks.AddRange(Enclosure(g, .07f, 0, 1, 3));
                level.blocks.Add(Block.Wall(Spot(.48f, .28f), Spot(.555f, .28f), R));
                level.blocks.Add(Block.Wall(Spot(.48f, .42f), Spot(.555f, .42f), R));
                level.mirrors = 2;
                level.solution.Add(Mirror(m1, e, m2));
                level.solution.Add(Mirror(m2, m1, g));
                return level;
            }
            case Template.FullCircle:
            {
                Vector4 e = Spot(-.1f, -.25f), g = Spot(.3f, .25f), ef = Heading(0, e);
                Vector4 m = NearestOnCircle(e, ef, Spot(.3f, -.25f));
                // the reflected beam heads straight away from the crystal, so it passes the antipode
                var level = New("Full Circle", "Everything you have learned. One mirror, one diffractor.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.AddRange(Enclosure(g, .085f, 0, 1, 2, 3));
                level.mirrors = 1;
                level.diffractors = 1;
                level.solution.Add(Mirror(m, e, g, true));
                level.solution.Add(Block.Diffractor(-g, Heading(0, -g), R));
                return level;
            }

            // ---------------------------------------------------------------- the Eternal Poles' locks

            case Template.FrozenMirror:
            {
                // the crystal only shows a narrow channel to the north, where a mirror is frozen into the ice;
                // a wall behind that mirror (running on across the laser) stops light from further north
                Vector4 e = Spot(0, -.3f), ef = Heading(0, e), g = Spot(.05f, .15f);
                Vector4 f = Offset(g, .26f, 0), m = NearestOnCircle(e, ef, f);
                var level = New("Frozen Mirror",
                    "That mirror is frozen into the ice: you can't move or turn it. " +
                    "The crystal only has a slot facing it. Bounce the laser off the frozen mirror.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.Add(FixedMirror(f, m, g, 1f));
                level.blocks.AddRange(Enclosure(g, .075f, 1, 2, 3));
                level.blocks.AddRange(Gap(g, .075f, 0, .025f));
                level.blocks.AddRange(Channel(g, .075f, .16f, 0, .025f));
                level.blocks.Add(Block.Wall(Offset(f, .06f, -.6f), Offset(f, .06f, .2f), R));
                level.mirrors = 1;
                level.solution.Add(Mirror(m, e, f));
                return level;
            }
            case Template.Prism:
            {
                // two crystals off the beam, 6 m out (beyond where the diffractor's raised rays clear them):
                // one square to the left, one 60° ahead to the right. Its ground rays (30° apart, lined up
                // with the light that hits it) reach both at once from only one spot on the beam
                Vector4 e = Spot(.1f, -.4f), ef = Heading(90, e);
                Vector4 d = NearestOnCircle(e, ef, Spot(.1f, .05f)), along = -S3.DirTo(d, e), right = S3Laser.Right(d, along) * (mirrorX ? -1 : 1);
                Vector4 a = S3.Walk(d, -right, .5f), b = S3.Walk(d, .5f * along + .866f * right, .5f);
                var level = New("Prism",
                    "No mirrors here. A diffractor also splits light along the ground into 12 beams, " +
                    "30° apart and lined up with the laser that hits it. Light both crystals at once.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(a, Heading(0, a), R));
                level.blocks.Add(Block.Goal(b, Heading(0, b), R));
                level.diffractors = 1;
                level.solution.Add(Block.Diffractor(d, along, R));
                return level;
            }
            case Template.TwinBeams:
            {
                // two lasers pointing opposite ways, each crystal on the far side of the other beam:
                // straight across, the two mirrors would sit in each other's light
                Vector4 e1 = Spot(-.05f, -.25f), f1 = Heading(0, e1), e2 = Spot(.65f, .25f), f2 = Heading(180, e2);
                Vector4 g1 = Spot(.3f, .5f), g2 = Spot(.3f, -.5f);
                Vector4 m1 = NearestOnCircle(e1, f1, Spot(.15f, -.25f)), m2 = NearestOnCircle(e2, f2, Spot(.45f, .25f));
                var level = New("Twin Beams",
                    "Two lasers, two crystals, two mirrors, and both crystals have to be lit at once. " +
                    "Careful: a mirror standing in the other beam's light will steal it.");
                level.blocks.Add(Block.Emitter(e1, f1, R));
                level.blocks.Add(Block.Emitter(e2, f2, R));
                level.blocks.Add(Block.Goal(g1, Heading(0, g1), R));
                level.blocks.Add(Block.Goal(g2, Heading(0, g2), R));
                level.blocks.AddRange(Enclosure(g1, .075f, 0, 1, 2));
                level.blocks.AddRange(Enclosure(g2, .075f, 0, 2, 3));
                level.mirrors = 2;
                level.solution.Add(Mirror(m1, e1, g1));
                level.solution.Add(Mirror(m2, e2, g2));
                return level;
            }
            case Template.Homecoming:
            {
                // the laser points straight away from the crystal, whose only opening faces east. Every great
                // circle through the crystal also passes its antipode, and the beam passes there too: the
                // only place a mirror can turn the light onto a path that comes home through the opening
                Vector4 g = Spot(.25f, .05f), e = Offset(g, .32f, 0), ef = -S3.DirTo(e, g);
                Vector4 eastAtG = S3Laser.Right(g, Heading(0, g)) * (mirrorX ? -1 : 1);
                float back = S3.Dist(e, g);
                Vector4 incoming = S3Laser.TangentAt(e, ef, Mathf.PI - back);
                var level = New("Homecoming",
                    "Walled in on three sides, and the laser points away. Every straight line through the crystal " +
                    $"also passes its antipode, {Mathf.PI * R:0.0} m away: a mirror there can send the light home from any direction.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.AddRange(Enclosure(g, .08f, 0, 2, 3));
                level.mirrors = 1;
                level.solution.Add(Block.Mirror(-g, (incoming - eastAtG).normalized, R));
                return level;
            }
            case Template.FrozenRelay:
            {
                // a frozen mirror stands across the mouth of the channel into the crystal and only turns light
                // down it if it arrives from the south-east; a wall cuts that line beyond where the relay mirror
                // goes (the laser meets it again round the far side of the world), so a second mirror has to relay
                Vector4 g = Spot(.3f, .25f), f = Offset(g, 0, -.24f);
                Vector4 u = S3.DirTo(f, Offset(f, -.25f, .144f));   // bearing 150° from the frozen mirror
                Vector4 p = S3.Walk(f, u, .12f), w = S3.Walk(f, u, .2f), across = S3Laser.Right(w, u);
                Vector4 e = Spot(.75f, -.25f), ef = Heading(180, e), m = NearestOnCircle(e, ef, p);
                var level = New("Frozen Relay",
                    "The frozen mirror only turns light down the channel if it arrives from one direction, " +
                    "and the wall cuts that line where the laser could reach it. Two mirrors.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.Add(FixedMirror(f, p, g, 1f));
                level.blocks.AddRange(Enclosure(g, .08f, 0, 1, 2));
                level.blocks.AddRange(Gap(g, .08f, 3, .025f));
                level.blocks.AddRange(Channel(g, .08f, .16f, 3, .025f));
                level.blocks.Add(Block.Wall(S3.Walk(w, across, .08f), S3.Walk(w, -across, .08f), R));
                level.mirrors = 2;
                level.solution.Add(Mirror(m, e, p));
                level.solution.Add(Mirror(p, m, f));
                return level;
            }
        }
        throw new System.ArgumentOutOfRangeException(nameof(template));
    }

    /// <summary>True if putting the solution pieces down lights every goal (scenery included).</summary>
    public static bool SolutionWorks(Level level, List<Block> obstacles)
    {
        var scene = new List<Block>(level.blocks);
        scene.AddRange(level.solution);
        return new S3Laser.Simulation(scene, obstacles).LightsAllGoals(scene);
    }

    /// <summary>True if the level is not already solved before any piece is placed.</summary>
    public static bool StartsUnsolved(Level level, List<Block> obstacles) =>
        !new S3Laser.Simulation(level.blocks, obstacles).LightsAllGoals(level.blocks);

    /// <summary>
    /// Ground points along the solved laser beam (and the pieces themselves), so world scenery can be
    /// kept off them. Of a diffractor's spray only the short ground rays are kept (the Prism's split
    /// beams): the rest fan out over the whole world, and the steep ones that reach the crystal only
    /// need the ground right around it clear (see CourseKeepOut).
    /// </summary>
    public static IEnumerable<Vector4> SolutionFootprint(Level level, float R, float spacing = 0.6f)
    {
        var scene = new List<Block>(level.blocks);
        scene.AddRange(level.solution);
        foreach (var p in LightFootprint(scene, R, spacing, true)) yield return p;
        foreach (var b in scene) yield return b.position;
        yield return level.start;
    }

    /// <summary>Ground points along the laser beam in a scene of blocks (diffracted light only if it's a short ray along the ground).</summary>
    public static IEnumerable<Vector4> LightFootprint(List<Block> scene, float R, float spacing = 0.6f, bool shortGroundRays = false)
    {
        var sim = new S3Laser.Simulation(scene, null);
        foreach (var s in sim.segments)
        {
            if (s.diffracted && !(shortGroundRays && s.length * R < 10f && Mathf.Abs(s.start.y) < 1e-4f && Mathf.Abs(s.dir.y) < 1e-4f)) continue;
            float len = Mathf.Min(s.length, S3Laser.Tau);
            int n = Mathf.CeilToInt(len * R / spacing);
            for (int k = 0; k <= n; k++)
            {
                Vector4 p = S3Laser.PointAt(s.start, s.dir, len * k / n);
                if (Mathf.Asin(Mathf.Clamp(p.y, -1, 1)) * R < 3f) yield return S3.Foot(p);
            }
        }
    }
}
