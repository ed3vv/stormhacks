using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Block = S3Laser.Block;
using Kind = S3Laser.Kind;

/// <summary>
/// The laser maze played on foot (port of the "laser" branch's puzzle.js): placing, carrying and
/// turning mirrors and diffractors, the hologram preview, the light, and drawing all of it.
///
///   * Look at the ground within reach and click to put down the piece in your hands. Its hologram
///     (and the light it would send) shows first. Q / E turn it, Shift for fine turns.
///   * Click a placed piece to pick it up again. 1 / 2 or the mouse wheel switch pieces.
/// </summary>
public class S3Puzzle
{
    const float ReachM = 5.4f;          // how far from your feet you can place or pick up
    const float PlayerRadiusM = 0.3f;
    const float TurnRate = 1.6f;        // radians per second

    static readonly Color BeamRed = new Color(1f, 0.06f, 0.04f);
    static readonly Color BeamGold = new Color(1f, 0.55f, 0.12f);
    const float BeamLift = 0.45f;       // the light is drawn this high above the ground it travels on

    readonly S3World world;
    readonly S3Player player;
    float R => world.radius;

    public S3Levels.Level Level { get; private set; }
    public readonly List<Block> blocks = new List<Block>();
    readonly List<Block> obstacles = new List<Block>();
    public int mirrorsLeft, diffractorsLeft;
    public Kind tool = Kind.Mirror;
    public bool Solved { get; private set; }

    /// <summary>Extra places pieces may not go (S3Golf: the sealed green, the ball).</summary>
    public Func<Vector4, bool> forbidden;

    int targetBlock = -1;
    Block ghost;
    bool ghostValid;
    float ghostAngle, lastRotation = -10;
    S3Laser.Simulation light = new S3Laser.Simulation(), preview = new S3Laser.Simulation();
    List<Block> previewScene;
    string flash;
    float flashUntil;

    // rendering
    readonly Dictionary<Block, Renderer> renderers = new Dictionary<Block, Renderer>();
    readonly List<(Renderer r, Matrix4x4 frame, float t)> sinking = new List<(Renderer, Matrix4x4, float)>();
    Renderer ghostRenderer, lightRenderer, previewRenderer;
    Kind ghostKind = Kind.Wall;
    Mesh lightMesh, previewMesh;
    readonly List<Vector3> bv = new List<Vector3>();
    readonly List<Color> bc = new List<Color>();
    readonly List<Vector4> b1 = new List<Vector4>(), b2 = new List<Vector4>(), b3 = new List<Vector4>();
    readonly List<int> bt = new List<int>();

    public S3Puzzle(S3World world, S3Player player)
    {
        this.world = world;
        this.player = player;
        foreach (var o in world.obstacles) obstacles.Add(Block.Scenery(o, R));
        lightMesh = NewBeamMesh("S3 Light");
        previewMesh = NewBeamMesh("S3 Light Preview");
        lightRenderer = world.CreateRenderer("Laser Light", lightMesh, world.beamMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        previewRenderer = world.CreateRenderer("Laser Preview", previewMesh, world.beamMaterial, Matrix4x4.identity).GetComponent<Renderer>();
    }

    public int Count(Kind k) => k == Kind.Mirror ? mirrorsLeft : diffractorsLeft;
    public Block Goal => blocks.Find(b => b.kind == Kind.Goal);

    /// <summary>The crystals the light isn't reaching yet (some locks have two).</summary>
    public IEnumerable<Block> UnlitGoals
    {
        get
        {
            for (int i = 0; i < blocks.Count; i++)
                if (blocks[i].kind == Kind.Goal && !light.litGoals.Contains(i)) yield return blocks[i];
        }
    }
    public List<Block> Obstacles => obstacles;

    // ------------------------------------------------------------------ level flow

    public void Load(S3Levels.Level level)
    {
        Clear();
        Level = level;
        blocks.AddRange(level.blocks);
        mirrorsLeft = level.mirrors;
        diffractorsLeft = level.diffractors;
        tool = level.mirrors > 0 ? Kind.Mirror : Kind.Diffractor;
        ghostAngle = 0;
        targetBlock = -1;
        ghost = null;
        foreach (var b in blocks) AddRenderer(b);
        TraceBeams();
    }

    /// <summary>A ring of ice walls around a sealed cup; they absorb light until the cup opens.</summary>
    public void AddGate(Vector4 cup, float radiusM, int segments)
    {
        foreach (var wall in GateWalls(cup, radiusM, segments, R))
        {
            blocks.Add(wall);
            AddRenderer(wall);
        }
        TraceBeams();
    }

    public static List<Block> GateWalls(Vector4 cup, float radiusM, int segments, float R)
    {
        var walls = new List<Block>();
        Vector4 f = S3.Ortho(S3.EZ, cup, S3.EY);
        if (f == Vector4.zero) f = S3.Ortho(S3.EX, cup, S3.EY);
        Vector4 r = S3Laser.Right(cup, f);
        const float gap = 0.18f;   // a little space between the blocks
        Vector4 Pt(float a) { Vector4 d = Mathf.Cos(a) * f + Mathf.Sin(a) * r; return S3.Walk(cup, d, radiusM / R); }
        for (int i = 0; i < segments; i++)
        {
            var wall = Block.Wall(Pt(S3Laser.Tau * (i + gap) / segments), Pt(S3Laser.Tau * (i + 1 - gap) / segments), R, 0.36f, 1.25f);
            wall.gate = true;
            walls.Add(wall);
        }
        return walls;
    }

    /// <summary>Sinks the gate walls into the ground and lets the light through.</summary>
    public void OpenGate()
    {
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            if (!blocks[i].gate) continue;
            if (renderers.TryGetValue(blocks[i], out var r) && r)
            {
                sinking.Add((r, blocks[i].Frame, 0));
                renderers.Remove(blocks[i]);
            }
            blocks.RemoveAt(i);
        }
        targetBlock = -1;
        TraceBeams();
    }

    public void Clear()
    {
        foreach (var r in renderers.Values) DestroyRenderer(r);
        renderers.Clear();
        foreach (var s in sinking) DestroyRenderer(s.r);
        sinking.Clear();
        blocks.Clear();
        Solved = false;
        ghost = null;
        if (ghostRenderer) ghostRenderer.enabled = false;
        light = new S3Laser.Simulation();
        preview = new S3Laser.Simulation();
        if (lightMesh) lightMesh.Clear();
        if (previewMesh) previewMesh.Clear();
    }

    /// <summary>Puts the intended solution down (capture tool / tests).</summary>
    public void DebugPlaceSolution()
    {
        foreach (var b in Level.solution) { blocks.Add(b); AddRenderer(b); }
        mirrorsLeft = diffractorsLeft = 0;
        TraceBeams();
    }

    public void Dispose()
    {
        Clear();
        DestroyRenderer(ghostRenderer);
        DestroyRenderer(lightRenderer);
        DestroyRenderer(previewRenderer);
    }

    // ------------------------------------------------------------------ per frame

    /// <summary>active: you're on foot with the mouse captured, so aiming and clicking act on the puzzle.</summary>
    public void Update(float dt, bool active)
    {
        if (active) HandleInput(dt);
        else { targetBlock = -1; ghost = null; }
        previewScene = null;
        if (ghost != null)
        {
            previewScene = new List<Block>(blocks) { ghost };
            preview = new S3Laser.Simulation(previewScene, obstacles, blocks.Count);
        }
        else preview = new S3Laser.Simulation();
        UpdateVisuals(dt);
    }

    void HandleInput(float dt)
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) Select(Kind.Mirror);
            if (kb.digit2Key.wasPressedThisFrame) Select(Kind.Diffractor);
            float turn = (kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0);
            if (turn != 0) Rotate(turn * TurnRate * (kb.leftShiftKey.isPressed ? 0.25f : 1f) * dt);
        }
        UpdateTargeting();
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f && Level != null && Level.mirrors > 0 && Level.diffractors > 0)
                Select(tool == Kind.Mirror ? Kind.Diffractor : Kind.Mirror);
            if (mouse.leftButton.wasPressedThisFrame && player.LockedOnFrame != Time.frameCount)
            {
                if (targetBlock >= 0) PickUp(); else Place();
            }
        }
    }

    void Select(Kind k)
    {
        tool = k;
        if (Count(k) == 0) Flash($"You have no {Name(k)}s here.");
    }

    static string Name(Kind k) => k == Kind.Mirror ? "mirror" : "diffractor";

    void Place()
    {
        if (Level == null) return;
        if (Count(tool) <= 0) { Flash($"No {Name(tool)}s left. Click a placed one to pick it up."); return; }
        if (ghost == null) { Flash("Aim at the ground within reach to place."); return; }
        if (!ghostValid) { Flash("Not enough room there."); return; }
        blocks.Add(ghost);
        AddRenderer(ghost);
        if (tool == Kind.Mirror) mirrorsLeft--; else diffractorsLeft--;
        ghost = null;
        TraceBeams();
        S3Audio.Click();
    }

    /// <summary>Lifts a placed piece back into your hands, keeping its orientation for the hologram.</summary>
    void PickUp()
    {
        var block = blocks[targetBlock];
        blocks.RemoveAt(targetBlock);
        if (renderers.TryGetValue(block, out var r)) { DestroyRenderer(r); renderers.Remove(block); }
        if (block.kind == Kind.Mirror) { mirrorsLeft++; tool = Kind.Mirror; }
        else { diffractorsLeft++; tool = Kind.Diffractor; }
        Vector4 away = AwayDirection(block.position);
        ghostAngle = Mathf.Atan2(Vector4.Dot(block.facing, S3Laser.Right(block.position, away)), Vector4.Dot(block.facing, away));
        targetBlock = -1;
        lastRotation = -10;
        TraceBeams();
        S3Audio.Click();
        Flash($"Picked up the {Name(tool)}. Click to put it down again.");
    }

    void Rotate(float angle)
    {
        lastRotation = Time.time;
        if (targetBlock >= 0)
        {
            var old = blocks[targetBlock];
            var turned = old.Rotated(angle, R);
            blocks[targetBlock] = turned;
            if (renderers.TryGetValue(old, out var r)) { renderers.Remove(old); renderers[turned] = r; S3World.SetFrame(r, turned.Frame); }
            TraceBeams();
        }
        else ghostAngle += angle;
    }

    /// <summary>Unit tangent at point pointing away from the player.</summary>
    Vector4 AwayDirection(Vector4 point)
    {
        Vector4 toPlayer = S3.DirTo(point, player.Foot);
        Vector4 away = toPlayer == Vector4.zero ? player.Forward : -toPlayer;
        Vector4 a = S3.Ortho(away, point, S3.EY);
        return a == Vector4.zero ? S3.Ortho(S3.EZ, point, S3.EY) : a;
    }

    void UpdateTargeting()
    {
        Vector4 foot = player.Foot;
        // a mirror turned edge-on slips out from under the crosshair; keep turning it
        if (targetBlock >= 0 && targetBlock < blocks.Count && Time.time - lastRotation < 0.3f &&
            S3.Dist(blocks[targetBlock].position, foot) * R < ReachM) return;
        targetBlock = -1;
        ghost = null;
        if (Level == null) return;
        Matrix4x4 view = player.ViewFrame();
        Vector4 x = view.GetColumn(3), d = view.GetColumn(2);
        int nearest = -1;
        float nearestT = float.PositiveInfinity;
        for (int i = 0; i < blocks.Count + obstacles.Count; i++)
        {
            var b = i < blocks.Count ? blocks[i] : obstacles[i - blocks.Count];
            if (b.Intersect(x, d, out var h) && h.t < nearestT) { nearestT = h.t; nearest = i; }
        }
        float groundT = S3Laser.GroundHit(x, d);
        if (nearest >= 0 && nearestT < groundT)
        {
            if (nearest < blocks.Count && blocks[nearest].movable && nearestT * R < ReachM + 1.7f) targetBlock = nearest;
            return;
        }
        if (groundT >= Mathf.PI) return;
        Vector4 point = S3.Foot(S3Laser.PointAt(x, d, groundT));
        if (S3.Dist(point, foot) * R > ReachM) return;
        // aiming at the ground right beside a thin mirror counts as aiming at it
        int beside = blocks.FindIndex(b => b.movable && b.FootprintContains(point, 0.25f / R));
        if (beside >= 0) { targetBlock = beside; return; }
        // with nothing left to place there is no hologram, only picking up
        if (Count(tool) <= 0) return;
        Vector4 facing = S3Laser.Turn(AwayDirection(point), point, ghostAngle);
        ghost = tool == Kind.Mirror ? Block.Mirror(point, facing, R) : Block.Diffractor(point, facing, R);
        float margin = 0.05f / R;
        ghostValid = !ghost.FootprintContains(foot, PlayerRadiusM / R)
                     && !blocks.Exists(b => ghost.FootprintOverlaps(b, margin))
                     && !obstacles.Exists(b => ghost.FootprintOverlaps(b, margin))
                     && (forbidden == null || !forbidden(point));
    }

    void TraceBeams()
    {
        light = new S3Laser.Simulation(blocks, obstacles);
        bool was = Solved;
        Solved = light.LightsAllGoals(blocks);
        BuildBeamMesh(lightMesh, light.segments, 1f);
        if (Solved && !was) Flash("");
    }

    /// <summary>True when putting the hologram down would light every goal.</summary>
    public bool HologramSolves => ghost != null && !Solved && previewScene != null && preview.LightsAllGoals(previewScene);

    /// <summary>Is a ground point blocked by a puzzle piece (for walking)?</summary>
    public bool BlocksPlayer(Vector4 foot)
    {
        foreach (var b in blocks)
            if (b.FootprintContains(foot, PlayerRadiusM / R)) return true;
        return false;
    }

    // ------------------------------------------------------------------ HUD text

    public string InventoryText
    {
        get
        {
            if (Level == null) return "";
            var parts = new List<string>();
            if (Level.mirrors > 0) parts.Add($"{(tool == Kind.Mirror ? "▶" : "  ")} [1] Mirror × {mirrorsLeft}");
            if (Level.diffractors > 0) parts.Add($"{(tool == Kind.Diffractor ? "▶" : "  ")} [2] Diffractor × {diffractorsLeft}");
            return string.Join("      ", parts);
        }
    }

    public string StatusText
    {
        get
        {
            if (flash != null && Time.realtimeSinceStartup < flashUntil && flash.Length > 0) return flash;
            if (HologramSolves) return "✦ Put it down here and the crystal lights up";
            var goal = Goal;
            // your own distance to the crystal is on its waypoint
            if (goal == null || Solved || ghost == null) return "";
            string text = $"Piece to crystal {S3.Dist(ghost.position, goal.position) * R:0.0} m";
            if (Level != null && Level.diffractors > 0) text += $"  ·  antipode {Mathf.PI * R:0.0} m";
            return text;
        }
    }

    public bool HasTarget => targetBlock >= 0;
    public bool HasGhost => ghost != null;

    void Flash(string text) { flash = text; flashUntil = Time.realtimeSinceStartup + 2.5f; }

    // ------------------------------------------------------------------ rendering

    void AddRenderer(Block b)
    {
        var mesh = S3Scenery.BlockMesh(b, world.biome);
        var r = world.CreateRenderer($"Laser {b.kind}", mesh, world.objectMaterial, b.Frame).GetComponent<Renderer>();
        renderers[b] = r;
    }

    static void DestroyRenderer(Renderer r)
    {
        if (!r) return;
        var mf = r.GetComponent<MeshFilter>();
        var mesh = mf ? mf.sharedMesh : null;
        if (Application.isPlaying) { UnityEngine.Object.Destroy(r.gameObject); if (mesh) UnityEngine.Object.Destroy(mesh); }
        else { UnityEngine.Object.DestroyImmediate(r.gameObject); if (mesh) UnityEngine.Object.DestroyImmediate(mesh); }
    }

    void UpdateVisuals(float dt)
    {
        float time = Time.time;
        // goals glow when lit, diffractors when light passes through them; the targeted piece pulses
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!renderers.TryGetValue(blocks[i], out var r) || !r) continue;
            float glow = 0;
            var k = blocks[i].kind;
            if (k == Kind.Goal) glow = light.litGoals.Contains(i) ? 1.1f + 0.35f * Mathf.Sin(time * 5) : 0;
            else if (k == Kind.Diffractor) glow = light.activeDiffractors.Contains(i) ? 0.9f + 0.3f * Mathf.Sin(time * 7) : 0;
            if (i == targetBlock) glow += 0.35f + 0.25f * Mathf.Sin(time * 8);
            S3World.SetLook(r, glow, 0, Color.white);
        }

        // gate walls sinking into the ground
        for (int i = sinking.Count - 1; i >= 0; i--)
        {
            var (r, frame, t) = sinking[i];
            t += dt;
            if (!r || t > 1.8f) { DestroyRenderer(r); sinking.RemoveAt(i); continue; }
            float depth = 1.5f * Mathf.SmoothStep(0, 1, t / 1.6f);
            S3World.SetFrame(r, frame * S3.PlaneRotation(S3.EW, S3.EY, -depth / R));
            sinking[i] = (r, frame, t);
        }

        // hologram
        if (ghost != null)
        {
            if (!ghostRenderer || ghostKind != ghost.kind)
            {
                DestroyRenderer(ghostRenderer);
                ghostRenderer = world.CreateRenderer("Laser Hologram", S3Scenery.BlockMesh(ghost, world.biome), world.objectMaterial, ghost.Frame).GetComponent<Renderer>();
                ghostKind = ghost.kind;
            }
            ghostRenderer.enabled = true;
            S3World.SetFrame(ghostRenderer, ghost.Frame);
            Color tint = !ghostValid ? new Color(1f, 0.35f, 0.3f) : HologramSolves ? new Color(1f, 0.9f, 0.4f) : new Color(0.55f, 1f, 0.95f);
            S3World.SetLook(ghostRenderer, 0.55f + 0.15f * Mathf.Sin(time * 6), 0.55f, tint);
        }
        else if (ghostRenderer) ghostRenderer.enabled = false;

        BuildBeamMesh(previewMesh, preview.segments, 0.45f);
    }

    static Mesh NewBeamMesh(string name)
    {
        var m = new Mesh { name = name };
        m.MarkDynamic();
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        return m;
    }

    /// <summary>
    /// Light as camera-facing ribbons. Each vertex carries its segment's start and direction and its
    /// distance along it; the S3/Beam shader puts it on the great circle and widens it towards the eye.
    /// </summary>
    void BuildBeamMesh(Mesh mesh, List<S3Laser.Segment> segments, float alpha)
    {
        if (!mesh) return;
        bv.Clear(); bc.Clear(); b1.Clear(); b2.Clear(); b3.Clear(); bt.Clear();
        const float step = 0.035f;   // radians between rungs (0.4 m)
        foreach (var s in segments)
        {
            float len = Mathf.Min(s.length, S3Laser.Tau);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / step));
            Color col = s.diffracted ? BeamGold : BeamRed;
            var info = new Vector4(s.diffracted ? 0.045f : 0.065f, BeamLift, alpha, 0);
            int start = bv.Count;
            for (int k = 0; k <= n; k++)
            {
                float t = len * k / n;
                for (int side = -1; side <= 1; side += 2)
                {
                    bv.Add(new Vector3(side, t, 0));
                    bc.Add(col);
                    b1.Add(s.start);
                    b2.Add(s.dir);
                    b3.Add(info);
                }
                if (k < n)
                {
                    int a = start + 2 * k;
                    bt.Add(a); bt.Add(a + 1); bt.Add(a + 2);
                    bt.Add(a + 1); bt.Add(a + 3); bt.Add(a + 2);
                }
            }
        }
        mesh.Clear();
        mesh.indexFormat = bv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(bv);
        mesh.SetColors(bc);
        mesh.SetUVs(1, b1);
        mesh.SetUVs(2, b2);
        mesh.SetUVs(3, b3);
        mesh.SetTriangles(bt, 0, false);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
    }
}
