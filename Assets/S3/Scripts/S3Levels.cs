using System.Collections.Generic;
using UnityEngine;
using Block = S3Laser.Block;

/// <summary>
/// The laser puzzles, adapted from the "laser" branch (src/game/laser/levels.js). Each one is a
/// template laid out around a frame: compass north is the frame's forward, east its right, and
/// spots are given as (north, east) in radians of arc, exactly as in the JS. The Desert plays them
/// as stand-alone puzzles. The Eternal Poles have their own set, the locks on its cups, and each of
/// those is built on something that can only happen in this closed, curved world: lines that start
/// out parallel meet, any two lines cross twice, a beam can be hopped back onto its own line from
/// half a world away, and a diffractor's light gathers again at its antipode.
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

    /// <summary>The first five are the Desert's; the rest are the Eternal Poles' own locks (the scene stores these as numbers).</summary>
    public enum Template { Reflection, LongWay, Antipode, Zigzag, FullCircle, ParallelLines, Crossfire, BackDoor, Starburst, LongNight }

    public static readonly Template[] DesertOrder =
        { Template.Reflection, Template.LongWay, Template.Antipode, Template.Zigzag, Template.FullCircle };

    /// <summary>
    /// Lays out one puzzle around frame (columns: east, up, north, origin). mirrorX flips east and west.
    /// variant picks between equivalent intended solutions (which spot or angle), so a lock can be fitted to its hole.
    /// </summary>
    public static Level Build(Template template, Matrix4x4 frame, float R, bool mirrorX = false, int variant = 0)
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
        // heading f at p turned clockwise (seen from above) by degrees; mirrored levels turn the other way
        Vector4 Turn(Vector4 f, Vector4 p, float degrees) => S3Laser.Turn(f, p, degrees * Mathf.Deg2Rad * (mirrorX ? -1 : 1));
        // horizontal unit tangent at p
        Vector4 Flat(Vector4 v, Vector4 p) => S3.Ortho(v, p, S3.EY);
        // the heading at distance t along the geodesic from p in direction d
        Vector4 Along(Vector4 p, Vector4 d, float t) => Flat(S3Laser.TangentAt(p, d, t), S3.Walk(p, d, t));
        // mirror at m, struck by a beam straight from source, sending it toward target (or directly away from it)
        Block Mirror(Vector4 m, Vector4 source, Vector4 target, bool away = false)
        {
            Vector4 incoming = -S3.DirTo(m, source), outgoing = S3.DirTo(m, target) * (away ? -1 : 1);
            return Block.Mirror(m, (incoming - outgoing).normalized, R);
        }
        // a mirror at m that turns light travelling in direction incoming into direction outgoing
        Block Turning(Vector4 m, Vector4 incoming, Vector4 outgoing, bool movable = true) =>
            new Block(S3Laser.Kind.Mirror, m, (incoming - outgoing).normalized, Block.MirrorSize, R, movable);
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
        // a point fwd ahead of c along heading f and right to its right (radians)
        Vector4 Off(Vector4 c, Vector4 f, float fwd, float right)
        {
            Vector4 v = f * fwd + S3Laser.Right(c, f) * right;
            float len = v.magnitude;
            return len < 1e-7f ? c : S3.Walk(c, v / len, len);
        }
        // walls along sides of a square around c facing f (0 front, 1 right, 2 back, 3 left)
        IEnumerable<Block> Pen(Vector4 c, Vector4 f, float r, params int[] sides)
        {
            Vector4 fr = Off(c, f, r, r), br = Off(c, f, -r, r), bl = Off(c, f, -r, -r), fl = Off(c, f, r, -r);
            var edges = new[] { (fl, fr), (fr, br), (br, bl), (bl, fl) };
            foreach (int s in sides) yield return Block.Wall(edges[s].Item1, edges[s].Item2, R);
        }
        // the front of such a pen with a slot of half-width gap, and walls running out from it to far, so only
        // light coming straight down the slot gets in
        IEnumerable<Block> Slot(Vector4 c, Vector4 f, float r, float gap, float far)
        {
            yield return Block.Wall(Off(c, f, r, -r), Off(c, f, r, -gap), R);
            yield return Block.Wall(Off(c, f, r, gap), Off(c, f, r, r), R);
            if (far <= r) yield break;
            yield return Block.Wall(Off(c, f, r, -gap), Off(c, f, far, -gap), R);
            yield return Block.Wall(Off(c, f, r, gap), Off(c, f, far, gap), R);
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

            case Template.ParallelLines:
            {
                // The frozen mirror only turns light into the crystal's slot if it comes from due north, down a
                // line parallel to the laser's. Both lines cross the east-west line square on, so they meet at its
                // pole, a quarter lap (18.8 m) north: the one place a single mirror can join them. Its antipode
                // works too (the long way round), and so can the laser's own antipode if nothing is in the way:
                // from there the light reaches the frozen mirror from behind, laps the world off it and comes
                // back in the front.
                Vector4 e = origin, ef = Heading(0, e), g = Spot(0, .25f), f = Spot(0, .6f), gEast = Heading(90, g);
                var level = New("Parallel Lines",
                    "The frozen mirror only turns light into the crystal if it arrives from due north, along a line " +
                    "parallel to your laser's beam. One mirror. On a flat world, parallel lines never meet.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, Heading(0, g), R));
                level.blocks.Add(Turning(f, -Heading(0, f), S3.DirTo(f, g), false));
                level.blocks.AddRange(Pen(g, gEast, .075f, 1, 2, 3));
                level.blocks.AddRange(Slot(g, gEast, .075f, .025f, .24f));
                level.mirrors = 1;
                Vector4 b = S3.Walk(e, ef, Mathf.PI / 2);
                level.solution.Add(Turning(b, S3Laser.TangentAt(e, ef, Mathf.PI / 2), S3.DirTo(b, f)));
                return level;
            }
            case Template.Crossfire:
            {
                // Two lasers stand back to back against an ice pillar and fire at right angles, away from it.
                // Their beams go round the world, cross again at the pillar's antipode and come home to hit the
                // pillar. One mirror at that far crossing, turned just so, reflects both beams back into the two
                // crystals; anywhere else it can only catch one of them.
                Vector4 x = origin, xn = Heading(0, x), ax = -x;
                Vector4 d1 = Heading(180, x), d2 = Heading(270, x);
                Vector4 e1 = S3.Walk(x, d1, .085f), e2 = S3.Walk(x, d2, .085f), f1 = Along(x, d1, .085f), f2 = Along(x, d2, .085f);
                Vector4 a1 = S3Laser.TangentAt(e1, f1, Mathf.PI - .085f), a2 = S3Laser.TangentAt(e2, f2, Mathf.PI - .085f);
                Vector4 n = Turn(Flat(a1, ax), ax, 22.5f + 15f * variant);
                Vector4 r1 = Flat(S3Laser.Reflect(a1, n), ax), r2 = Flat(S3Laser.Reflect(a2, n), ax);
                Vector4 g1 = S3.Walk(ax, r1, Mathf.PI - .3f), g2 = S3.Walk(ax, r2, Mathf.PI - .3f);
                Vector4 in1 = Along(ax, r1, Mathf.PI - .3f), in2 = Along(ax, r2, Mathf.PI - .3f);
                var level = New("Crossfire",
                    "Two lasers, two crystals, one mirror. The beams leave in different directions, but in this " +
                    "world any two straight lines cross twice.");
                level.start = S3.Walk(x, Heading(90, x), .2f);
                level.startFacing = Heading(270, level.start);
                level.blocks.Add(Block.Wall(S3.Walk(x, xn, -.002f), S3.Walk(x, xn, .002f), R, .7f, 1.3f));
                level.blocks.Add(Block.Emitter(e1, f1, R));
                level.blocks.Add(Block.Emitter(e2, f2, R));
                level.blocks.Add(Block.Goal(g1, in1, R));
                level.blocks.Add(Block.Goal(g2, in2, R));
                level.blocks.AddRange(Pen(g1, -in1, .075f, 1, 2, 3));
                level.blocks.AddRange(Pen(g2, -in2, .075f, 1, 2, 3));
                level.mirrors = 1;
                level.solution.Add(new Block(S3Laser.Kind.Mirror, ax, n, Block.MirrorSize, R, true));
                return level;
            }
            case Template.BackDoor:
            {
                // The crystal stands in the laser's path with its back to the beam, so light can only come in
                // the front after a lap of the world, and the crystal is in its own way. A mirror can take the
                // beam off its line, but the new line only meets the old one again at the mirror's antipode,
                // exactly half a world away: a second mirror there puts the light back on the laser's line,
                // heading home into the slot. (In a flat world two mirrors can't rejoin a line at all.) The wall
                // behind the laser stops the beam slipping round the long way at a shallow angle instead.
                Vector4 e = origin, ef = Heading(0, e), g = S3.Walk(e, ef, .42f), gf = Along(e, ef, .42f);
                var level = New("Back Door",
                    "The crystal faces away from the laser, and its own back blocks the beam: the way in is from the far " +
                    "side of the world. Two mirrors. A beam that leaves a straight line meets it again only once.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(g, gf, R));
                level.blocks.AddRange(Pen(g, gf, .075f, 1, 2, 3));
                level.blocks.AddRange(Slot(g, gf, .075f, .025f, .24f));
                level.blocks.Add(Block.Wall(Off(e, ef, -.06f, -.12f), Off(e, ef, -.06f, .12f), R));
                level.mirrors = 2;
                Vector4 m1 = S3.Walk(e, ef, .15f), inc = S3Laser.TangentAt(e, ef, .15f);
                Vector4 u = Turn(Flat(inc, m1), m1, 60 + 30 * variant);
                level.solution.Add(Turning(m1, inc, u));
                level.solution.Add(Turning(-m1, -u, S3.DirTo(-m1, g)));
                return level;
            }
            case Template.Starburst:
            {
                // One crystal is sealed in, so only sky light from a diffractor at its antipode can reach it, and
                // the laser doesn't pass there: a mirror has to aim the beam at that spot. Everything the diffractor
                // sends out gathers again on the sealed crystal, its ground rays converging like a starburst, 30°
                // apart and lined up with the light that hit it. The second crystal's slot faces along one spoke of
                // that starburst only if the beam comes in at the right angle, which depends on where the mirror
                // stands: 12 windows round the laser's line.
                Vector4 gs = origin, gsn = Heading(0, gs);
                Vector4 sd = Heading(40, gs), gc = S3.Walk(gs, sd, .42f), away = Along(gs, sd, .42f);
                Vector4 e = Spot(-.6f, 0), ef = Heading(90, e), d = -gs;
                var level = New("Starburst",
                    "One crystal is sealed in: only light falling from the sky can reach it. A diffractor's light gathers " +
                    "again 37.7 m away, raining down and converging from every side, its ground rays 30° apart and lined " +
                    "up with the beam that hit it. Turn that starburst until a ray finds the other crystal's slot.");
                level.start = e;
                level.startFacing = ef;
                level.blocks.Add(Block.Goal(gs, gsn, R));
                level.blocks.AddRange(Pen(gs, gsn, .085f, 0, 1, 2, 3));
                level.blocks.Add(Block.Goal(gc, away, R));
                level.blocks.AddRange(Pen(gc, away, .075f, 1, 2, 3));
                level.blocks.AddRange(Slot(gc, away, .075f, .06f, .17f));   // a wide, short nozzle: a few degrees of slack
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.mirrors = 1;
                level.diffractors = 1;
                var spots = StarburstSpots(e, ef, d, S3.DirTo(d, gc));
                if (spots.Count > 0)
                {
                    float t = spots[Mathf.Clamp(variant, 0, spots.Count - 1)];
                    Vector4 m = S3.Walk(e, ef, t);
                    level.solution.Add(Turning(m, S3Laser.TangentAt(e, ef, t), S3.DirTo(m, d)));
                }
                Vector4 df = Flat(S3.EZ, d);
                level.solution.Add(Block.Diffractor(d, df == Vector4.zero ? Flat(S3.EX, d) : df, R));
                return level;
            }
            case Template.LongNight:
            {
                // Back Door and the sky at once. The open crystal faces away from the laser in front of it; the
                // sealed one stands behind the laser, so its antipode is on the laser's own line, far round the
                // world, in a short tunnel that only admits light running along that line. The two mirrors hop the
                // beam onto the line half a world away, heading home; the diffractor in the tunnel rains light on
                // the sealed crystal and sends its straight-on ray the rest of the way into the open one.
                Vector4 e = origin, ef = Heading(0, e);
                Vector4 gc = S3.Walk(e, ef, .5f), gcf = Along(e, ef, .5f);
                Vector4 gs = S3.Walk(e, ef, -.25f), gsf = Along(e, ef, -.25f);
                Vector4 d = -gs, td = Along(e, ef, Mathf.PI - .25f);
                var level = New("The Long Night",
                    "Everything at once. One crystal faces away from the laser; the other is sealed behind it. " +
                    "Light passing through a diffractor carries straight on. Two mirrors, one diffractor.");
                level.blocks.Add(Block.Emitter(e, ef, R));
                level.blocks.Add(Block.Goal(gc, gcf, R));
                level.blocks.AddRange(Pen(gc, gcf, .075f, 1, 2, 3));
                level.blocks.AddRange(Slot(gc, gcf, .075f, .03f, 0));
                level.blocks.Add(Block.Goal(gs, gsf, R));
                level.blocks.AddRange(Pen(gs, gsf, .085f, 0, 1, 2, 3));
                level.blocks.Add(Block.Wall(Off(d, td, -.13f, .05f), Off(d, td, .13f, .05f), R));
                level.blocks.Add(Block.Wall(Off(d, td, -.13f, -.05f), Off(d, td, .13f, -.05f), R));
                level.mirrors = 2;
                level.diffractors = 1;
                Vector4 m1 = S3.Walk(e, ef, .15f), inc = S3Laser.TangentAt(e, ef, .15f);
                Vector4 u = Turn(Flat(inc, m1), m1, 60 + 30 * variant);
                level.solution.Add(Turning(m1, inc, u));
                level.solution.Add(Turning(-m1, -u, S3.DirTo(-m1, gc)));
                level.solution.Add(Block.Diffractor(d, td, R));
                return level;
            }
        }
        throw new System.ArgumentOutOfRangeException(nameof(template));
    }

    /// <summary>
    /// Starburst: distances along the laser's line (from e, heading ef) where a mirror sends the beam to d
    /// arriving at a multiple of 30° to heading s at d, so one of d's ground rays leaves along s.
    /// </summary>
    public static List<float> StarburstSpots(Vector4 e, Vector4 ef, Vector4 d, Vector4 s)
    {
        // building every variant of one placement (S3Setup.SearchLocks) asks the same question repeatedly
        if (lastSpots != null && lastSpotsKey.Equals((e, ef, d, s))) return lastSpots;
        var spots = new List<float>();
        float Error(float t)
        {
            Vector4 m = S3.Walk(e, ef, t), toD = S3.DirTo(m, d);
            if (toD == Vector4.zero) return float.NaN;
            Vector4 a = S3.Ortho(S3Laser.TangentAt(m, toD, S3.Dist(m, d)), d, S3.EY);
            float angle = Mathf.Atan2(Vector4.Dot(a, S3Laser.Right(d, s)), Vector4.Dot(a, s)) * Mathf.Rad2Deg;
            return Mathf.Repeat(angle + 15f, 30f) - 15f;
        }
        const int samples = 1500;
        const float t0 = 0.05f, span = 2 * Mathf.PI - 0.1f;
        float prev = Error(t0);
        for (int i = 1; i <= samples; i++)
        {
            float t = t0 + span * i / samples, cur = Error(t);
            // a real crossing, not the jump from +15° to -15°
            if (!float.IsNaN(prev) && !float.IsNaN(cur) && Mathf.Sign(prev) != Mathf.Sign(cur) && Mathf.Abs(prev - cur) < 10f)
            {
                float lo = t - span / samples, hi = t;
                for (int k = 0; k < 30; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (Mathf.Sign(Error(mid)) == Mathf.Sign(prev)) lo = mid; else hi = mid;
                }
                spots.Add((lo + hi) / 2);
            }
            prev = cur;
        }
        lastSpotsKey = (e, ef, d, s);
        lastSpots = spots;
        return spots;
    }

    static (Vector4, Vector4, Vector4, Vector4) lastSpotsKey;
    static List<float> lastSpots;

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
    /// kept off them. Of a diffractor's spray only the ground rays that light a crystal, or are short, are
    /// kept: the rest fan out over the whole world, and the steep ones that reach the crystal only
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

    /// <summary>
    /// Ground points along the laser beam in a scene of blocks. Diffracted light only if requested and it's a
    /// ray along the ground that is short or ends on a crystal.
    /// </summary>
    public static IEnumerable<Vector4> LightFootprint(List<Block> scene, float R, float spacing = 0.6f, bool groundRays = false)
    {
        var sim = new S3Laser.Simulation(scene, null);
        var goals = scene.FindAll(b => b.kind == S3Laser.Kind.Goal);
        foreach (var s in sim.segments)
        {
            float len = Mathf.Min(s.length, S3Laser.Tau);
            if (s.diffracted)
            {
                bool ground = Mathf.Abs(s.start.y) < 1e-4f && Mathf.Abs(s.dir.y) < 1e-4f;
                if (!groundRays || !ground) continue;
                Vector4 end = S3.Foot(S3Laser.PointAt(s.start, s.dir, len));
                if (s.length * R >= 10f && !goals.Exists(g => g.FootprintContains(end, 0.1f / R))) continue;
            }
            int n = Mathf.CeilToInt(len * R / spacing);
            for (int k = 0; k <= n; k++)
            {
                Vector4 p = S3Laser.PointAt(s.start, s.dir, len * k / n);
                if (Mathf.Asin(Mathf.Clamp(p.y, -1, 1)) * R < 3f) yield return S3.Foot(p);
            }
        }
    }
}
