using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Golf on the ground great-sphere of S^3, with the controls and feel of the ed3vv
/// spherical-golf prototype (github.com/ed3vv/stormhacks):
///
///   * The camera sits behind the ball, facing the shot. A/D or moving the mouse left/right turn
///     the shot direction; W/S set the launch height (2°..90°); Shift for fine control. Moving
///     the mouse up/down tilts the view freely without touching the aim; C levels it again.
///   * Space starts the power meter oscillating; Space again shoots. Right-click cancels.
///   * One club: low launches roll like putts, high ones fly. About 80% power straight up crosses
///     the pole of the hemisphere (pi/2 * R overhead) and lands on the far side of the world:
///     gravity fades with height (gravityFalloff), so the climb costs less than it would at full strength.
///   * After every shot a low camera follows the ball; once it stops, the camera glides to it and
///     you address it again. Tab steps away from the ball to walk around, and F (E on the Meadow)
///     glides you back to it from anywhere.
///
/// The campaign runs Meadow (golf) → Desert (laser puzzles on foot, no ball) → Eternal Poles, where
/// every cup is sealed under an ice barrier until you light the crystal beside it with the laser
/// (see S3Puzzle); there each hole starts on foot beside the ball with a mirror in hand, and F
/// addresses the ball whenever you want to golf. Mirrors you place are solid, so the ball bounces off them. M jumps between maps,
/// L opens the level select to jump straight to any hole or puzzle, and Esc pauses (with the controls).
/// The HUD and menus are drawn by S3HudView (UI Toolkit); this class pushes its state there every frame.
///
/// Physics constants are ed3vv's, which are in unit-sphere units; multiplying by the world
/// radius R gives the same trajectories at this world's size with the same timing.
/// </summary>
[DefaultExecutionOrder(10)]
public class S3Golf : MonoBehaviour
{
    public S3Player player;

    [Header("Ball (ed3vv proportions: 0.026 R)")]
    public float ballRadius = 0.31f;

    [Header("Physics in ed3vv units (scaled by the world radius)")]
    public float gravityUnits = 1.32f;        // R/s^2
    [Tooltip("0: gravity is the same at every height. 1: it fades to nothing at the pole, so a straight-up shot needs less power to go over and come down on the far side of the world.")]
    [Range(0, 1)] public float gravityFalloff = 1f;
    public float rollFrictionUnits = 0.54f;   // R/s^2 rolling deceleration
    public float bounceSpeedUnits = 0.14f;    // below this landing speed the ball rolls instead of bouncing
    public float restitution = 0.38f;
    public float bounceGrip = 0.9f;
    public float airDrag = 0.035f;            // per second
    [Tooltip("Shots play back this much faster than real time. Where the ball goes doesn't change.")]
    public float shotSpeed = 1.2f;

    [Header("Aim and power")]
    public float minAngle = 2f;
    public float maxAngle = 90f;
    public float startAngle = 24f;
    public float aimDegreesPerSecond = 60f;
    public float heightDegreesPerSecond = 35f;
    [Tooltip("Speed multiplier while Shift is held, for fine aiming.")]
    public float fineAimFactor = 0.25f;
    public float powerCycleRate = 3.2f;       // the meter swings 0 -> 1 -> 0 every 2 pi / rate seconds
    [Tooltip("After a shot the aim faces roughly towards the cup, off by this many degrees (random, either side), so you still line it up yourself.")]
    public float autoAimOffsetMin = 4f;
    public float autoAimOffsetMax = 12f;

    [Tooltip("How close (metres) you must walk to your ball to address it.")]
    public float reach = 2.5f;

    [Header("Ball camera")]
    public float cameraBack = 3.6f;           // metres behind the ball (ed3vv: 0.30 R)
    public float cameraHeight = 1.9f;         // eye height (ed3vv: 0.16 R)

    [Header("Flight camera: hangs back low, looking up at the ball (always centred)")]
    [Tooltip("Camera height above the ground (metres).")]
    public float lowHeight = 0.7f;
    [Tooltip("How far behind the ball's ground track the camera sits (metres), plus lowBackPerMetre for every metre the ball climbs, up to lowMaxBack.")]
    public float lowBack = 3.5f;
    public float lowBackPerMetre = 0.45f;
    public float lowMaxBack = 8f;
    [Tooltip("Degrees off straight behind the ball, for a three-quarter view.")]
    public float lowSideAngle = 25f;
    [Tooltip("How tightly the camera chases its spot; lower is lazier and lags further behind fast shots.")]
    public float lowFollowRate = 4f;
    [Tooltip("How quickly the camera swings round when the ball changes direction (off a wall, say).")]
    public float headingRate = 2.5f;
    [Tooltip("Above this ball height (metres) the camera hands over to the trailing camera, which can follow a shot over the pole.")]
    public float highShotHeight = 12f;
    [Tooltip("Seconds the handover to and from the trailing camera takes.")]
    public float highShotBlend = 0.5f;
    [Header("Trailing camera for very high shots")]
    public float leash = 4.1f;                // trails the ball at most this far (ed3vv: 0.34 R)
    public float followMinHeight = 1.0f;
    [Tooltip("How quickly the view rolls to keep 'up' away from the ground (crossing the pole turns it over).")]
    public float lookRate = 5f;
    public float settleTime = 0.7f;

    [Header("Glide to the ball")]
    [Tooltip("Pause after the ball stops before the camera glides to it (seconds).")]
    public float glideDelay = 0.6f;
    [Tooltip("Glide time: this many seconds, plus glideSecondsPerMetre for every metre travelled, up to glideMaxTime.")]
    public float glideMinTime = 0.8f;
    public float glideSecondsPerMetre = 0.04f;
    public float glideMaxTime = 2.2f;
    [Tooltip("The glide arcs up by this fraction of its length (up to glideMaxArc metres) to clear buildings.")]
    public float glideArc = 0.25f;
    public float glideMaxArc = 6f;

    [Header("Title screen")]
    [Tooltip("Open on the title screen (the world turning as a glass ball, Play / Level select).")]
    public bool showTitle = true;

    [Header("Sinking it")]
    [Tooltip("Seconds after the ball drops before the card with Replay and Next comes up.")]
    public float resultDelay = 1.2f;
    [Tooltip("Shots that hole out from at least this far (metres) can be watched again from outside the world. 0: every one.")]
    public float replayMinDistance = 0f;

    [Header("Sound (S3Audio)")]
    [Tooltip("A shot that passes this close to the cup (metres, over it or beside it) and carries on plays the miss jingle.")]
    public float missRadius = 1.5f;

    enum State { Ready, Moving, Sunk, Finished }

    const float Step = 1f / 240f;

    S3World world;
    S3Ball ball;
    State state;
    Renderer ballRenderer, flagRenderer, arrowRenderer, guideRenderer, restRenderer;
    Mesh restMesh;
    // a beacon where a shot straight over the pole comes down
    Renderer beaconRenderer;
    MaterialPropertyBlock glowBlock;
    bool steepShot;                       // the last shot was hit steeply enough to be going over the pole
    readonly List<S3Laser.Block> solids = new List<S3Laser.Block>();   // what the ball bounces off: puzzle pieces and fences
    float stopTime = 1e6f;        // seconds since the ball last came to rest after a shot
    // sinking it: the ball drops into the cup, the flag hops, a ring and confetti burst out, then the result card
    float sinkTime;
    Vector4 sinkFoot;             // where the ball was when the cup took it
    string sinkName = "";         // "Birdie!" etc.
    bool resultShown;
    Matrix4x4 flagFrame;
    S3Confetti confetti;
    // instant replay of the hole (S3Replay)
    S3Replay replay;
    List<Vector4> track = new List<Vector4>();            // this shot's flight, one sample every S3Replay.Sample seconds
    readonly List<List<Vector4>> holeShots = new List<List<Vector4>>();   // every shot on this hole, in order
    float sinceSample, sinkLength;
    Renderer sinkRingRenderer;
    Mesh sinkRingMesh;
    int holeIndex, strokes, total;
    bool powerSelecting, exploring;
    float power, powerPhase, accumulator, moveTime, aimAngle;
    float shotMaxHeight;
    float startToCup, closestToCup;   // this shot's distance to the cup when hit, and the nearest it has come
    bool missed;                      // this shot has already played the miss jingle
    Vector4 lastShot;
    float lookPitch;   // free vertical look while addressing the ball (radians, relative to the shot)
    Vector4 aimDir;      // shot direction: horizontal unit tangent at the ball's foot
    Vector4 viewDir;     // the ball camera's heading, eased towards aimDir while the ball moves
    bool wasGrounded;
    // flight camera
    float followWeight;
    // gliding to the ball
    bool gliding, glidePending;
    float glideT, glideTime, glideWait, glideLength, glideDegrees;
    Vector3 glideAxis;
    Matrix4x4 glideFrom;
    Vector4 camPos, camUp, camFwd;   // trailing camera (very high shots)
    Vector4 lowPos, flightDir;       // the low cinematic camera, and the ball's heading along the ground
    float highWeight;                // 0 = low camera, 1 = trailing camera
    Matrix4x4 followFrame;
    S3HudView hud;
    int[] holeStrokes = Array.Empty<int>();   // strokes on each hole this round (0 = not played)
    float lastPower = -1;                     // the last shot's power, ghosted on the meter
    // laser puzzles (Desert, Basin)
    S3Puzzle puzzle;
    int puzzleIndex;              // Desert: which puzzle
    bool locked;                  // Basin: the cup is sealed until its crystal is lit
    int lockHole = -1;            // Basin: the hole whose lock is loaded
    Renderer domeRenderer;
    MaterialPropertyBlock domeBlock;
    float domeFade;
    bool advancing;               // counting down to the next puzzle or map
    float advanceTimer;
    bool showHint;
    float hintUntil;
    static readonly S3World.Biome[] Maps = { S3World.Biome.Meadow, S3World.Biome.Desert, S3World.Biome.Basin };

    S3World.Hole Hole => world.holes[holeIndex];
    float R => world.radius;

    bool titleUp;   // the title screen is showing

    void Start()
    {
        if (Application.isPlaying) S3Graphics.Apply(player ? player.GetComponent<Camera>() : Camera.main);
        Init();
        if (showTitle && Application.isPlaying) ShowTitle();
    }

    void OnDestroy() => S3Graphics.Restore();

    /// <summary>G or the pause menu: Quality (render scale 1.5, SMAA) or Performance (1.0, MSAA only).</summary>
    void ToggleGraphics()
    {
        var mode = S3Graphics.Toggle(player.GetComponent<Camera>());
        S3Audio.Click();
        if (hud.Current == S3HudView.Menu.Pause) OpenPause();   // refresh its button
        else if (hud.Current == S3HudView.Menu.None)
            Say($"{S3Graphics.Name(mode)} mode\n" + (mode == S3Graphics.Mode.Quality ? "Render scale 1.5  ·  4× MSAA + SMAA" : "Render scale 1.0  ·  4× MSAA  ·  about 2× the frame rate"), 2.5f);
    }

    // ------------------------------------------------------------------ title screen

    /// <summary>The title over the world turning as a glass ball (the replay camera's view, showing the first green).</summary>
    void ShowTitle()
    {
        titleUp = true;
        PauseForMenu();
        if (replay != null && ball != null) replay.BeginShowcase(Hole.cup, flagFrame);
        hud.ShowTitle(StartFromTitle, OpenLevels);
    }

    void EndTitle()
    {
        if (!titleUp) return;
        titleUp = false;
        replay?.End();
    }

    /// <summary>Play: the first hole (or puzzle) from the start, intro banner and all.</summary>
    void StartFromTitle()
    {
        EndTitle();
        CloseMenus();
        if (world.HasGolf) StartHole(holeIndex);
        else StartPuzzle(puzzleIndex);
    }

    void UpdateTitle()
    {
        replay?.Update(Time.deltaTime, Vector2.zero);
        var kb = Keyboard.current;
        if (kb == null) return;
        if (hud.Current == S3HudView.Menu.Levels)
        {
            if (kb.lKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) CloseMenus();   // back to the title
            return;
        }
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) StartFromTitle();
        else if (kb.lKey.wasPressedThisFrame) OpenLevels();
    }

    /// <summary>Creates the ball, flag and aim helpers and starts hole 1 (also used by the capture tool).</summary>
    public void Init()
    {
        world = S3World.Instance;
        if (!player) player = FindAnyObjectByType<S3Player>();
        hud = S3HudView.Ensure(gameObject, player);
        hud.SetBiome(world.biome);
        if (hud.Current == S3HudView.Menu.Results) hud.CloseMenu();
        lastPower = -1;
        CloseResult();
        replay?.Dispose();
        replay = null;
        player.cameraFilter = ViewFor;
        player.inputEnabled = false;
        exploring = false;
        advancing = false;
        followWeight = 0;
        total = 0;
        lockHole = -1;
        locked = false;
        puzzle?.Dispose();
        puzzle = null;
        if (world.HasLasers) puzzle = new S3Puzzle(world, player) { forbidden = Forbidden };
        player.blocked = p => (puzzle != null && puzzle.BlocksPlayer(p)) || world.FenceAt(p, player.bodyRadius);
        if (!world.HasGolf)
        {
            // the Desert: puzzles on foot, no ball
            ball = null;
            state = State.Ready;
            StartPuzzle(0);
            return;
        }
        holeStrokes = new int[world.holes.Count];
        ball = NewBall(R);
        ball.radius = ballRadius;
        ConfigureBall(ball);
        ballRenderer = world.CreateRenderer("Golf Ball", S3Scenery.BallMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        flagRenderer = world.CreateRenderer("Flag", S3Scenery.FlagMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        arrowRenderer = world.CreateRenderer("Aim Arrow", S3Scenery.ArrowMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        guideRenderer = world.CreateRenderer("Launch Guide", S3Scenery.GuideMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        restMesh = S3Scenery.RingMesh(48, new Color(0.25f, 0.9f, 1f, 0.4f));
        restRenderer = world.CreateRenderer("Rest Ring", restMesh, world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        sinkRingMesh = S3Scenery.RingMesh(64, new Color(1f, 0.86f, 0.4f, 0.3f));
        sinkRingRenderer = world.CreateRenderer("Sink Ring", sinkRingMesh, world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        sinkRingRenderer.enabled = false;
        confetti = new S3Confetti(world);
        if (Application.isPlaying) replay = new S3Replay(world, player.GetComponent<Camera>());
        stopTime = 1e6f;
        if (world.glowMaterial)
            beaconRenderer = world.CreateRenderer("Antipode Beacon", S3Scenery.BeaconMesh(), world.glowMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        domeRenderer = puzzle != null && world.glowMaterial
            ? world.CreateRenderer("Ice Barrier", S3Scenery.DomeMesh(world.barrierRadius), world.glowMaterial, Matrix4x4.identity).GetComponent<Renderer>()
            : null;
        StartHole(0);
    }

    // ------------------------------------------------------------------ laser puzzles

    void StartPuzzle(int index)
    {
        puzzleIndex = index;
        var level = world.levels[index];
        puzzle.Load(level);
        player.Teleport(level.start, level.startFacing, -0.15f);
        exploring = true;
        player.inputEnabled = true;
        advancing = false;
        showHint = false;   // the level's hint stays tucked away until you press H
        Say($"Puzzle {index + 1}/{world.levels.Count}\n{level.title}", 3f);
    }

    void ShowHint(float seconds) { showHint = true; hintUntil = Time.realtimeSinceStartup + seconds; }

    /// <summary>Places a piece may not go: on the sealed green, on the cup, on the ball.</summary>
    bool Forbidden(Vector4 p)
    {
        if (!world.HasGolf || ball == null) return false;
        float toCup = world.GroundDistance(p, Hole.cup);
        if (toCup < world.cupRadius + 1.2f || (locked && toCup < world.barrierRadius + 0.4f)) return true;
        return world.GroundDistance(p, ball.Foot) < ballRadius + 0.7f;
    }

    void Unlock()
    {
        locked = false;
        puzzle.OpenGate();
        S3Audio.Win();
        Say($"{(Crystals > 1 ? "Crystals" : "Crystal")} lit!\nThe cup is open", 3.5f, true);
    }

    /// <summary>How many crystals the loaded lock has (some have two).</summary>
    int Crystals
    {
        get
        {
            int n = 0;
            if (puzzle?.Level != null)
                foreach (var b in puzzle.Level.blocks)
                    if (b.kind == S3Laser.Kind.Goal) n++;
            return n;
        }
    }
    string TheCrystal => Crystals > 1 ? "the crystals" : "the crystal";

    /// <summary>While sealed, the barrier dome over the green turns the ball away.</summary>
    void CollideBarrier()
    {
        Vector4 c = Hole.cup;
        float d = (ball.pos - c).magnitude * R, min = world.barrierRadius + ballRadius;
        if (d >= min) return;
        Vector4 n = S3.Ortho(ball.pos - c, ball.pos);
        if (n == Vector4.zero) return;
        ball.pos = S3.Walk(ball.pos, n, (min - d) / R);
        ball.frame.SetColumn(3, ball.pos);
        float vn = Vector4.Dot(ball.vel, n);
        if (vn < 0) { S3Audio.Land(-vn); ball.vel -= 1.5f * vn * n; }
        ball.vel = S3.Tangent(ball.pos, ball.vel);
    }

    /// <summary>Next map in the campaign (or the M key's cycle).</summary>
    static S3World.Biome NextMap(S3World.Biome b) =>
        b == S3World.Biome.Meadow ? S3World.Biome.Desert : b == S3World.Biome.Desert ? S3World.Biome.Basin : S3World.Biome.Meadow;

    void GoToMap(S3World.Biome b)
    {
        world.SetBiome(b);
        Init();
        string intro = b == S3World.Biome.Desert ? "Desert Oasis\nNo golf here: light the crystals with the laser."
                     : b == S3World.Biome.Basin ? "Eternal Poles\nUnlock each cup with the laser, then sink your ball."
                     : "Meadow";
        Say(intro, 4f);
    }

    // ------------------------------------------------------------------ level selector

    static string MapTitle(S3World.Biome b) =>
        b == S3World.Biome.Desert ? "Desert Oasis" : b == S3World.Biome.Basin ? "Eternal Poles" : "Meadow";

    bool MenuOpen => hud != null && (hud.Current == S3HudView.Menu.Pause || hud.Current == S3HudView.Menu.Levels);

    /// <summary>The game (and the walker) is paused while the pause menu or level select is up.</summary>
    void PauseForMenu()
    {
        player.paused = true;
        powerSelecting = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void OpenPause()
    {
        PauseForMenu();
        hud.ShowPause(world.HasGolf ? "Restart hole" : "Restart puzzle", CloseMenus, () => { CloseMenus(); Restart(); }, OpenLevels,
                      $"Graphics: {S3Graphics.Name(S3Graphics.Current)}", ToggleGraphics);
    }

    void OpenLevels()
    {
        PauseForMenu();
        hud.ShowLevels(BuildLevelColumns(), Array.IndexOf(Maps, world.biome), world.HasGolf ? holeIndex : puzzleIndex, (m, i) => JumpTo(Maps[m], i));
    }

    /// <summary>Back to the game; a finished round goes back to its scorecard.</summary>
    void CloseMenus()
    {
        hud.CloseMenu();
        if (titleUp) { hud.ShowTitle(StartFromTitle, OpenLevels); return; }
        if (ball != null && state == State.Sunk && resultShown) { ShowResult(); return; }
        player.paused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (ball != null && state == State.Finished) ShowResults();
    }

    /// <summary>R: the current hole (or puzzle) from the start.</summary>
    void Restart()
    {
        if (!world.HasGolf) { StartPuzzle(puzzleIndex); return; }
        if (state == State.Sunk && resultShown) { ReplayHole(); return; }
        if (state == State.Sunk || state == State.Finished) return;
        total -= strokes;
        StartHole(holeIndex);
    }

    S3HudView.LevelColumn[] BuildLevelColumns()
    {
        S3HudView.LevelEntry Hole(S3World.Biome map, S3World.HoleDef h, int i, string title)
        {
            float d = S3.Dist(S3.GroundPoint(h.tee.x, h.tee.y, R), S3.GroundPoint(h.cup.x, h.cup.y, R)) * R;
            return new S3HudView.LevelEntry(title ?? $"Hole {i + 1}", $"Par {h.par}  ·  {d:0} m", S3Progress.Best(map, i), false);
        }
        var meadow = new S3HudView.LevelEntry[world.course.Length];
        for (int i = 0; i < meadow.Length; i++) meadow[i] = Hole(S3World.Biome.Meadow, world.course[i], i, null);
        var desert = new S3HudView.LevelEntry[S3Levels.DesertOrder.Length];
        for (int i = 0; i < desert.Length; i++)
        {
            var level = S3Levels.Build(S3Levels.DesertOrder[i], Matrix4x4.identity, R);
            string Count(int n, string what) => n > 0 ? $"{n} {what}{(n > 1 ? "s" : "")}" : null;
            string pieces = string.Join("  ·  ", new[] { Count(level.mirrors, "mirror"), Count(level.diffractors, "diffractor") }.Where(x => x != null));
            desert[i] = new S3HudView.LevelEntry(level.title, pieces, S3Progress.Best(S3World.Biome.Desert, i), true);
        }
        var basin = new S3HudView.LevelEntry[world.basinCourse.Length];
        for (int i = 0; i < basin.Length; i++)
            basin[i] = Hole(S3World.Biome.Basin, world.basinCourse[i], i, i < world.basinLocks.Length ? world.BuildLock(world.basinLocks[i]).title : null);
        return new[]
        {
            new S3HudView.LevelColumn { title = MapTitle(S3World.Biome.Meadow), sub = "Golf", modifier = "", entries = meadow },
            new S3HudView.LevelColumn { title = MapTitle(S3World.Biome.Desert), sub = "Laser puzzles", modifier = "level-col-head--desert", entries = desert },
            new S3HudView.LevelColumn { title = MapTitle(S3World.Biome.Basin), sub = "Lasers and golf", modifier = "level-col-head--basin", entries = basin },
        };
    }

    /// <summary>The scorecard at the end of a golf map.</summary>
    void ShowResults()
    {
        var par = new int[world.holes.Count];
        for (int i = 0; i < par.Length; i++) par[i] = world.holes[i].par;
        bool meadow = world.biome == S3World.Biome.Meadow;
        hud.ShowResults(meadow ? "Meadow complete" : world.biome == S3World.Biome.Basin ? "Campaign complete" : $"{world.MapName} complete",
            par, holeStrokes,
            meadow ? new[] { ("Enter", "On to the Desert Oasis") } : new[] { ("N", "Play again"), ("L", "Level select") });
    }

    /// <summary>Start a given hole (or Desert puzzle) on a map, from scratch.</summary>
    void JumpTo(S3World.Biome b, int index)
    {
        EndTitle();
        CloseMenus();
        if (b != world.biome) { world.SetBiome(b); Init(); }   // rebuilding clears the old map's renderers
        advancing = false;
        if (world.HasGolf) { total = 0; lockHole = -1; holeStrokes = new int[world.holes.Count]; StartHole(index); }
        else StartPuzzle(index);
    }

    void UpdateAdvance(float dt)
    {
        if (!advancing) return;
        advanceTimer -= dt;
        if (advanceTimer > 0) return;
        advancing = false;
        if (!world.HasGolf && puzzleIndex + 1 < world.levels.Count) StartPuzzle(puzzleIndex + 1);
        else GoToMap(NextMap(world.biome));
    }

    void AdvanceIn(float seconds) { advancing = true; advanceTimer = seconds; }

    /// <summary>The Desert: walking and puzzles only.</summary>
    void UpdatePuzzleMap(float dt)
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.mKey.wasPressedThisFrame) { GoToMap(NextMap(world.biome)); return; }
            if (kb.rKey.wasPressedThisFrame) StartPuzzle(puzzleIndex);
            if (kb.nKey.wasPressedThisFrame) StartPuzzle(0);
            if (kb.hKey.wasPressedThisFrame) { if (showHint && Time.realtimeSinceStartup < hintUntil) showHint = false; else ShowHint(1e6f); }
            if (kb.enterKey.wasPressedThisFrame && advancing) advanceTimer = 0;
        }
        bool active = Cursor.lockState == CursorLockMode.Locked && !advancing;
        puzzle.Update(dt, active);
        if (puzzle.Solved && !advancing)
        {
            S3Audio.Win();
            bool last = puzzleIndex + 1 >= world.levels.Count;
            S3Progress.Record(world.biome, puzzleIndex, 1);
            Say(last ? "All puzzles solved!\nNext: the Eternal Poles" : "Crystal lit!", 3.5f, true);
            AdvanceIn(last ? 4.5f : 3f);
        }
        UpdateAdvance(dt);
        Shader.SetGlobalFloat("_S3Fade", 1);
        Vector4 eye = player.ViewFrame().GetColumn(3);
        world.UpdateFades(eye, eye, dt);
    }

    /// <summary>A ball with the default (ed3vv) physics for a world of radius R.</summary>
    public static S3Ball NewBall(float R) => new S3Ball
    {
        R = R, radius = 0.026f * R, gravity = 1.32f * R, restitution = 0.38f,
        bounceGrip = 0.9f, bounceSpeed = 0.14f * R, airDrag = 0.035f,
    };

    void ConfigureBall(S3Ball b)
    {
        b.gravity = gravityUnits * R;
        b.heightFalloff = gravityFalloff;
        b.restitution = restitution;
        b.bounceGrip = bounceGrip;
        b.bounceSpeed = bounceSpeedUnits * R;
        b.airDrag = airDrag;
    }

    /// <summary>ed3vv's launch speed: gentle low end, and a p^4 kick so full power can cross the pole.</summary>
    public static float LaunchSpeed(float p, float R) => R * (0.42f + 1.18f * p + 0.7f * p * p * p * p);

    void StartHole(int index)
    {
        if (hud != null && (hud.Current == S3HudView.Menu.Results || hud.Current == S3HudView.Menu.HoleDone)) hud.CloseMenu();
        holeIndex = index;
        strokes = 0;
        aimAngle = startAngle;
        followWeight = 0;
        powerSelecting = false;
        ball.Place(Hole.tee);
        lastShot = ball.Foot;
        state = State.Ready;
        world.SetActiveCup(Hole.cup);
        world.SetActiveHole(index);
        Vector4 toTee = S3.DirTo(Hole.cup, Hole.tee);
        flagFrame = S3.FrameAt(Hole.cup, S3.EY, toTee == Vector4.zero ? S3.EZ : toTee);
        S3World.SetFrame(flagRenderer, flagFrame);
        CloseResult();
        replay?.End();
        track = new List<Vector4>();
        holeShots.Clear();
        sinkTime = 0;
        confetti?.Hide();
        if (sinkRingRenderer) sinkRingRenderer.enabled = false;
        aimDir = viewDir = DirectionToCup();
        lookPitch = 0;
        exploring = false;
        gliding = glidePending = false;
        player.inputEnabled = false;
        // Basin: seal the cup and load its lock (a restart keeps a lock you already opened)
        if (puzzle != null && lockHole != index && index < world.levels.Count)
        {
            lockHole = index;
            puzzle.Load(world.levels[index]);
            puzzle.AddGate(Hole.cup, world.barrierRadius - 0.45f, 12);
            locked = true;
            domeFade = 1;
        }
        stopTime = 1e6f;
        if (puzzle != null)
        {
            // Eternal Poles: start on foot beside the ball with a mirror in hand; F addresses the ball whenever you like
            Vector4 stand = S3.Walk(ball.Foot, -aimDir, 1.6f / R);
            Vector4 facing = S3.Ortho(aimDir, stand, S3.EY);
            player.Teleport(stand, facing == Vector4.zero ? aimDir : facing, -0.25f);
            exploring = true;
            player.inputEnabled = true;
            if (puzzle.mirrorsLeft > 0) puzzle.tool = S3Laser.Kind.Mirror;
        }
        float dist = world.GroundDistance(Hole.tee, Hole.cup);
        Say($"Hole {index + 1}  ·  Par {Hole.par}  ·  {dist:0} m" +
            (dist > 0.97f * Mathf.PI * R ? "\nThe cup is at your antipode: every direction leads there." : "") +
            (!locked && world.holeFences.ContainsKey(index) ? "\nThe green is fenced off on this side: come in from behind, over the pole or round the world." : "") +
            (locked ? $"\nSealed in ice: light {TheCrystal}. F to golf whenever you're ready." : ""), 5.5f);
        showHint = false;
        UpdateVisuals();
    }

    Vector4 DirectionToCup()
    {
        Vector4 foot = ball.Foot;
        Vector4 d = S3.DirTo(foot, Hole.cup);
        if (d == Vector4.zero) d = S3.Ortho(S3.EZ, foot, S3.EY);
        if (d == Vector4.zero) d = S3.Ortho(S3.EX, foot, S3.EY);
        return d;
    }

    /// <summary>Near the cup but not on it: the aim a shot leaves you with, a few degrees to one side.</summary>
    Vector4 RoughDirectionToCup()
    {
        float off = UnityEngine.Random.Range(autoAimOffsetMin, autoAimOffsetMax) * (UnityEngine.Random.value < 0.5f ? -1 : 1);
        return Turned(DirectionToCup(), ball.Foot, off * Mathf.Deg2Rad);
    }

    void Update()
    {
        if (world == null) return;
        if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) ToggleGraphics();
        if (titleUp) { UpdateTitle(); return; }
        if (replay != null && replay.Active) { UpdateReplay(); return; }   // it takes the whole screen; nothing else moves
        var keys = Keyboard.current;
        if (keys != null)
        {
            if (keys.lKey.wasPressedThisFrame) { if (hud.Current == S3HudView.Menu.Levels) CloseMenus(); else OpenLevels(); }
            else if (keys.escapeKey.wasPressedThisFrame) { if (MenuOpen) CloseMenus(); else OpenPause(); }
        }
        if (MenuOpen) return;
        if (!world.HasGolf) { if (puzzle != null) UpdatePuzzleMap(Time.deltaTime); return; }
        if (ball == null) return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float dt = Time.deltaTime;
        bool mouseLocked = Cursor.lockState == CursorLockMode.Locked;
        Vector2 md = mouse != null && mouseLocked ? mouse.delta.ReadValue() : Vector2.zero;

        if (kb != null)
        {
            // F or Tab glides back to the ball from anywhere (E too on the Meadow; on laser maps E turns pieces)
            bool address = kb.fKey.wasPressedThisFrame || (puzzle == null && kb.eKey.wasPressedThisFrame);
            if (kb.tabKey.wasPressedThisFrame || address)
            {
                if (!exploring) { if (kb.tabKey.wasPressedThisFrame) SetExploring(true); }
                else if (state == State.Ready)
                {
                    // standing at the ball: aim where you were facing as you walked up; from afar: at the cup
                    Vector4 facing = NearBall ? S3.Ortho(player.Forward, ball.Foot, S3.EY) : Vector4.zero;
                    aimDir = viewDir = facing != Vector4.zero ? facing : RoughDirectionToCup();
                    lookPitch = 0;
                    GlideToBall();
                    S3Audio.Click();
                }
            }
            if (kb.mKey.wasPressedThisFrame) { GoToMap(NextMap(world.biome)); return; }
            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.nKey.wasPressedThisFrame) { total = 0; lockHole = -1; advancing = false; holeStrokes = new int[world.holes.Count]; StartHole(0); }
            if (kb.hKey.wasPressedThisFrame && puzzle != null) { if (showHint && Time.realtimeSinceStartup < hintUntil) showHint = false; else ShowHint(1e6f); }
            if (kb.enterKey.wasPressedThisFrame && advancing) advanceTimer = 0;
            if (kb.bKey.wasPressedThisFrame && (state == State.Ready || state == State.Moving) && strokes > 0)
            {
                ball.Place(lastShot);
                state = State.Ready;
                followWeight = 0;
                powerSelecting = false;
                strokes++; total++;
                aimDir = viewDir = RoughDirectionToCup();
                lookPitch = 0;
                GlideToBall();
                Say("Ball returned\n+1 stroke", 2.5f);
            }
            if (kb.spaceKey.wasPressedThisFrame && state == State.Ready && !exploring && !gliding)
            {
                if (!powerSelecting) { powerSelecting = true; powerPhase = 0; power = 0; S3Audio.Click(); }
                else Shoot(power);
            }
        }
        if (mouse != null && mouse.rightButton.wasPressedThisFrame) powerSelecting = false;

        // keep the headings attached to the ball as it moves (projection = parallel transport for small steps)
        Vector4 foot = ball.Foot;
        Vector4 a = S3.Ortho(aimDir, foot, S3.EY);
        if (a != Vector4.zero) aimDir = a;
        Vector4 v = S3.Ortho(viewDir, foot, S3.EY);
        viewDir = v != Vector4.zero ? v : aimDir;

        if (state == State.Ready && !exploring && !gliding)
        {
            float fine = kb != null && kb.leftShiftKey.isPressed ? fineAimFactor : 1f;
            float turn = kb == null ? 0 : (Pressed(kb.dKey, kb.rightArrowKey) - Pressed(kb.aKey, kb.leftArrowKey));
            float lift = kb == null ? 0 : (Pressed(kb.wKey, kb.upArrowKey) - Pressed(kb.sKey, kb.downArrowKey));
            // A/D and mouse left/right both turn the shot (locked once the power meter runs)
            if (!powerSelecting)
                aimDir = Turned(aimDir, foot, turn * aimDegreesPerSecond * fine * dt * Mathf.Deg2Rad + md.x * player.lookSensitivity * fine);
            viewDir = aimDir;
            aimAngle = Mathf.Clamp(aimAngle + lift * heightDegreesPerSecond * fine * dt, minAngle, maxAngle);
            // mouse up/down only tilts the view
            lookPitch = Mathf.Clamp(lookPitch + md.y * player.lookSensitivity, -1.45f - CameraPitch(), MaxViewPitch - CameraPitch());
            if (kb != null && kb.cKey.wasPressedThisFrame) lookPitch = 0;
            if (powerSelecting)
            {
                powerPhase += dt;
                power = 0.5f - 0.5f * Mathf.Cos(powerPhase * powerCycleRate);
            }
        }
        else viewDir = Turned(viewDir, foot, SignedAngle(viewDir, aimDir, foot) * (1 - Mathf.Exp(-6f * dt)));

        UpdateGlide(dt);

        switch (state)
        {
            case State.Moving: Simulate(dt); break;
            case State.Sunk:
                sinkTime += dt;
                if (!resultShown && sinkTime >= resultDelay) ShowResult();
                else if (resultShown && kb != null)
                {
                    if (kb.wKey.wasPressedThisFrame && CanWatchReplay) WatchReplay();
                    else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) NextHole();
                }
                break;
        }
        UpdateSinkEffects(dt);
        if (puzzle != null)
        {
            puzzle.Update(dt, exploring && mouseLocked && state != State.Finished);
            if (locked && puzzle.Solved) Unlock();
        }
        UpdateBarrier(dt);
        UpdateAdvance(dt);
        if (ball == null) return;   // finishing the Meadow just moved on to the Desert, which has no ball
        UpdateFollow(dt);
        stopTime += dt;
        UpdateVisuals();
        UpdateSeeThrough(dt);
    }

    void UpdateBarrier(float dt)
    {
        if (!domeRenderer) return;
        domeFade = Mathf.MoveTowards(domeFade, locked ? 1 : 0, dt / 1.4f);
        domeRenderer.enabled = domeFade > 0.01f && state != State.Finished;
        if (!domeRenderer.enabled) return;
        S3World.SetFrame(domeRenderer, S3.FrameAt(Hole.cup, S3.EY, S3.EZ) * S3.PlaneRotation(S3.EW, S3.EY, -(1 - domeFade) * 1.2f / R));
        domeBlock ??= new MaterialPropertyBlock();
        var block = domeBlock;
        domeRenderer.GetPropertyBlock(block);
        block.SetFloat("_Intensity", domeFade * (0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f)));
        block.SetFloat("_Shimmer", 1f);
        domeRenderer.SetPropertyBlock(block);
    }

    void UpdateSeeThrough(float dt)
    {
        Shader.SetGlobalFloat("_S3Fade", 1);
        world.UpdateFades(player.ViewFrame().GetColumn(3), ball.pos, dt);
    }

    static float Pressed(UnityEngine.InputSystem.Controls.KeyControl a, UnityEngine.InputSystem.Controls.KeyControl b) =>
        a.isPressed || b.isPressed ? 1f : 0f;

    /// <summary>Rotate a horizontal heading at foot by angle radians (positive = right).</summary>
    static Vector4 Turned(Vector4 dir, Vector4 foot, float angle)
    {
        if (angle == 0) return dir;
        Vector4 right = S3.FrameAt(foot, S3.EY, dir).GetColumn(0);
        return (Mathf.Cos(angle) * dir + Mathf.Sin(angle) * right).normalized;
    }

    static float SignedAngle(Vector4 from, Vector4 to, Vector4 foot)
    {
        Vector4 right = S3.FrameAt(foot, S3.EY, from).GetColumn(0);
        return Mathf.Atan2(Vector4.Dot(to, right), Vector4.Dot(to, from));
    }

    bool NearBall => world.GroundDistance(player.Foot, ball.Foot) < reach;

    /// <summary>On foot (true) or addressing the ball with the aim camera (false).</summary>
    void SetExploring(bool on, string message = null)
    {
        if (on == exploring) return;
        gliding = glidePending = false;
        if (on)
        {
            // step out of the ball camera: stand where it was, looking the same way
            Matrix4x4 c = BallCamera();
            Vector4 eyeFoot = S3.Foot(c.GetColumn(3));
            Vector4 fwd = S3.Ortho(c.GetColumn(2), eyeFoot, S3.EY);
            player.Teleport(eyeFoot, fwd == Vector4.zero ? S3.Ortho(viewDir, eyeFoot, S3.EY) : fwd, Mathf.Clamp(CameraPitch() + lookPitch, -1.4f, 1.4f));
            powerSelecting = false;
        }
        exploring = on;
        player.inputEnabled = on;
        if (message != null) Say(message, 2.5f);
    }

    Vector4 ShotDirection => S3.Ortho(aimDir, ball.pos, S3.UpAt(ball.pos));

    void Shoot(float p)
    {
        powerSelecting = false;
        lastPower = p;
        lastShot = ball.Foot;
        ball.Launch(ShotDirection, LaunchSpeed(p, R), aimAngle * Mathf.Deg2Rad);
        steepShot = aimAngle >= 60;
        S3Audio.Hit(p);
        strokes++; total++;
        state = State.Moving;
        moveTime = accumulator = 0;
        shotMaxHeight = 0;
        wasGrounded = false;
        startToCup = closestToCup = S3.Dist(ball.Foot, Hole.cup) * R;
        missed = false;
        track = new List<Vector4> { ball.pos };
        holeShots.Add(track);
        sinceSample = 0;
        SetExploring(true);   // watch it go; the camera glides to it once it stops
    }

    void Simulate(float dt)
    {
        accumulator += Mathf.Min(dt, 0.1f) * shotSpeed;
        moveTime += dt;
        float friction = rollFrictionUnits * R;
        solids.Clear();
        if (puzzle != null) solids.AddRange(puzzle.blocks);
        solids.AddRange(world.fences);
        while (accumulator >= Step)
        {
            accumulator -= Step;
            ball.Step(Step, friction, world.obstacles, solids);
            sinceSample += Step;
            if (sinceSample >= S3Replay.Sample) { sinceSample -= S3Replay.Sample; track.Add(ball.pos); }
            S3Audio.Land(ball.impact);
            if (locked) CollideBarrier();
            shotMaxHeight = Mathf.Max(shotMaxHeight, ball.HeightM - ballRadius);

            // like ed3vv: after landing, the camera turns to follow the way the ball is travelling
            if (ball.grounded && !wasGrounded)
            {
                Vector4 travel = S3.Ortho(ball.vel, ball.Foot, S3.EY);
                if (travel != Vector4.zero && ball.Speed > 0.3f) aimDir = travel;
            }
            wasGrounded = ball.grounded;

            // the cup takes the ball whenever it rolls or lands in it (ed3vv has no lip-out)
            float toCup = S3.Dist(ball.Foot, Hole.cup) * R;
            if (!locked && toCup < world.cupRadius && ball.HeightM < ballRadius * 1.3f)
            {
                state = State.Sunk;
                sinkTime = 0;
                sinkFoot = ball.Foot;
                track.Add(ball.pos);
                sinkLength = world.GroundDistance(lastShot, ball.Foot);
                int diff = strokes - Hole.par;
                sinkName = strokes == 1 ? "Hole in one!" :
                    diff <= -2 ? "Eagle!" : diff == -1 ? "Birdie!" : diff == 0 ? "Par" : diff == 1 ? "Bogey" : diff == 2 ? "Double bogey" : $"+{diff}";
                holeStrokes[holeIndex] = strokes;
                S3Progress.Record(world.biome, holeIndex, strokes);
                hud.HideBanner();
                S3Audio.Sunk(strokes, Hole.par);
                return;
            }

            // went over the cup (or just beside it) and carried on past: the miss jingle, once a shot
            closestToCup = Mathf.Min(closestToCup, toCup);
            if (!missed && !locked && closestToCup < missRadius && closestToCup < startToCup - 0.25f && toCup > closestToCup + 0.6f)
            {
                missed = true;
                S3Audio.Lose();
            }
            if (ball.grounded && ball.Speed < 0.05f)
            {
                ball.vel = Vector4.zero;
                state = State.Ready;
                stopTime = 0;
                QueueGlide();
                if (shotMaxHeight > 0.5f * Mathf.PI * R - 1f && world.GroundDistance(lastShot, ball.Foot) > 0.5f * Mathf.PI * R)
                    Say("Over the pole!\nIt came down on the far side of the world", 3.5f, true);
                return;
            }
        }
        if (moveTime > 40f) { ball.vel = Vector4.zero; state = State.Ready; stopTime = 0; QueueGlide(); }
    }

    // ------------------------------------------------------------------ sinking it

    const float BurstAt = 0.3f;   // seconds after the ball reaches the cup: it has dropped out of sight

    /// <summary>The hole-complete card: free the mouse for its buttons and hold the walker still.</summary>
    void ShowResult()
    {
        resultShown = true;
        player.paused = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        bool last = holeIndex + 1 >= world.holes.Count;
        hud.ShowHoleResult(holeIndex + 1, sinkName, strokes <= Hole.par,
            $"{strokes} {(strokes == 1 ? "stroke" : "strokes")}  ·  Par {Hole.par}  ·  Total {total}",
            CanWatchReplay ? WatchReplay : null, ReplayHole, NextHole, last ? "Finish" : "Next hole");
    }

    void CloseResult()
    {
        if (!resultShown) return;
        resultShown = false;
        if (hud != null && hud.Current == S3HudView.Menu.HoleDone) hud.CloseMenu();
        player.paused = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Play the hole just sunk again; its strokes come off the total.</summary>
    void ReplayHole()
    {
        total -= strokes;
        holeStrokes[holeIndex] = 0;
        StartHole(holeIndex);
    }

    /// <summary>On to the next hole, or the map's scorecard after the last one.</summary>
    void NextHole()
    {
        CloseResult();
        if (holeIndex + 1 < world.holes.Count) { StartHole(holeIndex + 1); return; }
        state = State.Finished;
        ShowResults();
        if (world.biome == S3World.Biome.Meadow) AdvanceIn(10f);
    }

    /// <summary>The flag hops and wobbles, a gold ring spreads over the green, and confetti bursts out of the cup.</summary>
    void UpdateSinkEffects(float dt)
    {
        if (confetti == null) return;
        bool sunk = state == State.Sunk;
        if (sunk && sinkTime >= BurstAt && sinkTime - dt < BurstAt)
        {
            confetti.Burst(S3.FrameAt(Hole.cup, S3.EY, S3.EZ));
            S3Audio.Click();
        }
        confetti.Update(dt);

        float u = sinkTime - BurstAt;
        float ring = u / 0.9f;
        sinkRingRenderer.enabled = sunk && ring > 0 && ring < 1;
        if (sinkRingRenderer.enabled)
        {
            float e = 1 - (1 - ring) * (1 - ring) * (1 - ring);
            S3Scenery.SetRing(sinkRingMesh, Mathf.Lerp(0.3f, world.greenRadius, e), 0.35f * (1 - ring));
            S3World.SetFrame(sinkRingRenderer, S3.FrameAt(Hole.cup, S3.EY, S3.EZ));
        }

        Matrix4x4 f = flagFrame;
        if (sunk && u > 0)
        {
            float hop = u < 0.45f ? 0.4f * Mathf.Sin(Mathf.PI * u / 0.45f) : 0;
            float wobble = 0.22f * Mathf.Exp(-3.5f * u) * Mathf.Sin(16f * u);
            f = flagFrame * S3.PlaneRotation(S3.EW, S3.EY, hop / R) * S3.PlaneRotation(S3.EY, S3.EX, wobble);
        }
        S3World.SetFrame(flagRenderer, f);
    }

    /// <summary>The ball rolling to the middle of the cup and dropping in, spinning.</summary>
    Matrix4x4 SinkFrame()
    {
        float slide = Mathf.SmoothStep(0, 1, sinkTime / 0.2f);
        float fall = Mathf.Clamp01((sinkTime - 0.06f) / 0.3f);
        float depth = 2.6f * ballRadius * fall * fall;
        Vector4 d = S3.DirTo(sinkFoot, Hole.cup);
        Vector4 foot = d == Vector4.zero ? sinkFoot : S3.Walk(sinkFoot, d, S3.Dist(sinkFoot, Hole.cup) * slide);
        Vector4 c = S3.Lift(foot, (ballRadius - depth) / R);
        return S3.FrameAt(c, S3.UpAt(c), d == Vector4.zero ? S3.Ortho(S3.EZ, c, S3.EY) : d) * S3.PlaneRotation(S3.EZ, S3.EY, sinkTime * 9f);
    }

    // ------------------------------------------------------------------ instant replay

    bool CanWatchReplay => replay != null && holeShots.Exists(s => s.Count > 1) && sinkLength >= replayMinDistance;

    /// <summary>Watch every shot of the hole again, from outside the world, each tracing its path (the mouse orbits; click to come back).</summary>
    void WatchReplay()
    {
        Mesh fence = world.fenceRenderers.TryGetValue(holeIndex, out var r) && r ? r.GetComponent<MeshFilter>().sharedMesh : null;
        replay.Begin(holeShots, Hole.cup, flagFrame, fence);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        S3Audio.Click();
    }

    void UpdateReplay()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        Vector2 md = mouse != null && Cursor.lockState == CursorLockMode.Locked ? mouse.delta.ReadValue() : Vector2.zero;
        replay.Update(Time.deltaTime, md);
        bool leave = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                     (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame));
        if (!leave || replay.Age < 0.25f) return;
        // back to the hole-complete card
        replay.End();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        S3Audio.Click();
    }

    string ReplayLine()
    {
        string shots = replay.Shots > 1 ? $"Shot {replay.CurrentShot + 1} of {replay.Shots}" : "One shot";
        return $"Hole {holeIndex + 1}  ·  {shots}  ·  {S3Replay.Speed}× speed";
    }

    // ------------------------------------------------------------------ cameras

    /// <summary>ed3vv's pitch rule: tilt up as the launch height rises, keeping the ball in view.</summary>
    float CameraPitch() => Mathf.Clamp(-0.30f + 0.26f * (aimAngle - 24f) * Mathf.Deg2Rad, -0.85f, 0.42f);

    /// <summary>The view may tilt all the way up, to look straight over the pole.</summary>
    const float MaxViewPitch = Mathf.PI / 2;
    /// <summary>From this view pitch (radians, about 66°) up to straight up, the camera eases forward to stand over the ball.</summary>
    const float OverheadFrom = 1.15f;

    /// <summary>0 normally, 1 when the ball camera looks straight up and stands right over the ball.</summary>
    float Overhead => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(OverheadFrom, MaxViewPitch, CameraPitch() + lookPitch));

    /// <summary>
    /// The standing golfer view: behind the ball along viewDir, looking past it. Tilted towards straight up it
    /// eases forward until it stands right over the ball, because straight up from there is exactly the ball's
    /// antipode, where a straight-up shot comes down (from behind the ball, the antipode shows up far off to one side).
    /// </summary>
    Matrix4x4 BallCamera()
    {
        Vector4 foot = ball.Foot;
        if (state == State.Sunk || state == State.Finished) foot = Hole.cup;
        Vector4 dir = S3.Ortho(viewDir, foot, S3.EY);
        if (dir == Vector4.zero) dir = DirectionToCup();
        float back = cameraBack * (1 - Overhead) / R;
        Vector4 camFoot = S3.Walk(foot, -dir, back);
        Vector4 fwd = Mathf.Sin(back) * foot + Mathf.Cos(back) * dir;   // transported heading
        return S3.FrameAt(camFoot, S3.EY, fwd)
             * S3.PlaneRotation(S3.EW, S3.EY, cameraHeight / R)
             * S3.PlaneRotation(S3.EZ, S3.EY, CameraPitch() + lookPitch);
    }

    /// <summary>Camera filter for the player: ball camera (or walking view), blended with the flight camera.</summary>
    Matrix4x4 ViewFor(Matrix4x4 walkingCamera)
    {
        Matrix4x4 baseFrame = exploring ? walkingCamera : BallCamera();
        if (gliding) baseFrame = GlideFrame(baseFrame);
        if (followWeight <= 0) return baseFrame;
        Matrix4x4 follow = followFrame;
        float s = Mathf.SmoothStep(0, 1, followWeight);
        if (s >= 1) return follow;
        Vector4 a = baseFrame.GetColumn(3), b = follow.GetColumn(3);
        Vector4 pos = S3.Walk(a, S3.DirTo(a, b), S3.Dist(a, b) * s);
        Vector4 up = Vector4.Lerp(baseFrame.GetColumn(1), follow.GetColumn(1), s);
        Vector4 fwd = Vector4.Lerp(baseFrame.GetColumn(2), follow.GetColumn(2), s);
        return S3.FrameAt(pos, up, fwd);
    }

    void UpdateFollow(float dt)
    {
        // follow the ball from the shot until the glide back to it (or the next hole) takes over
        bool want = state == State.Moving || state == State.Sunk || state == State.Finished || glidePending;
        if (want && followWeight <= 0)
        {
            Matrix4x4 c = player.ViewFrame();
            camPos = lowPos = c.GetColumn(3);
            camUp = c.GetColumn(1);
            camFwd = c.GetColumn(2);
            flightDir = aimDir;
            highWeight = 0;
            followFrame = c;
        }
        followWeight = Mathf.MoveTowards(followWeight, want ? 1 : 0, dt / (want ? 0.4f : settleTime));
        if (followWeight > 0)
        {
            TrackBall(dt);
            followFrame = FlightFrame(dt);
        }
    }

    /// <summary>
    /// The cinematic flight camera: low to the ground, behind and a little to the side of the ball's
    /// path, looking up at it. Very high shots blend over to the trailing camera, which can follow
    /// the ball over the pole (there "behind the ball along the ground" stops meaning anything).
    /// </summary>
    Matrix4x4 FlightFrame(float dt)
    {
        Matrix4x4 trailing = S3.FrameAt(camPos, camUp, camFwd);
        float h = ball.HeightM - ballRadius;
        highWeight = Mathf.MoveTowards(highWeight, h > highShotHeight ? 1 : 0, dt / highShotBlend);
        float high = Mathf.SmoothStep(0, 1, highWeight);
        if (high >= 0.999f) { lowPos = camPos; return trailing; }   // pick up from here on the way down

        // high up, the ball's ground point means little (it jumps across the world at the pole): hold still
        if (h <= highShotHeight)
        {
            // the ball's heading along the ground, swinging round smoothly when it changes (bounces)
            Vector4 foot = ball.Foot;
            Vector4 heading = S3.Ortho(flightDir, foot, S3.EY);
            if (heading == Vector4.zero) heading = DirectionToCup();
            Vector4 v = ball.vel - Vector4.Dot(ball.vel, foot) * foot - Vector4.Dot(ball.vel, S3.EY) * S3.EY;
            if (v.magnitude > 0.5f)
                heading = Turned(heading, foot, SignedAngle(heading, v.normalized, foot) * (1 - Mathf.Exp(-headingRate * dt)));
            flightDir = heading;

            // the spot to chase: behind the ball, off to the side, near the ground; further back the higher it flies
            float back = Mathf.Min(lowBack + lowBackPerMetre * Mathf.Max(0, h), lowMaxBack);
            float side = lowSideAngle * Mathf.Deg2Rad;
            Vector4 right = S3.FrameAt(foot, S3.EY, flightDir).GetColumn(0);
            Vector4 away = -Mathf.Cos(side) * flightDir + Mathf.Sin(side) * right;
            Vector4 spot = S3.Lift(S3.Walk(foot, away, back / R), lowHeight / R);
            float d = S3.Dist(lowPos, spot);
            Vector4 toSpot = S3.DirTo(lowPos, spot);
            if (d > 1e-6f && toSpot != Vector4.zero) lowPos = S3.Walk(lowPos, toSpot, d * (1 - Mathf.Exp(-lowFollowRate * dt)));
            if (Mathf.Asin(Mathf.Clamp(lowPos.y, -1f, 1f)) * R < 0.3f) lowPos = S3.Lift(S3.Foot(lowPos), 0.3f / R);
        }

        // look straight at the ball, keeping the horizon level; looking nearly straight up, where
        // "level" stops meaning anything, keep the last frame's roll so the picture doesn't spin
        Vector4 look = S3.DirTo(lowPos, ball.pos);
        if (look == Vector4.zero) look = S3.Ortho(followFrame.GetColumn(2), lowPos);
        Vector4 level = S3.UpAt(lowPos) - Vector4.Dot(S3.UpAt(lowPos), look) * look;
        Vector4 kept = S3.Ortho(followFrame.GetColumn(1), lowPos, look);
        float steady = Mathf.Clamp01(level.magnitude / 0.35f);
        Vector4 up = kept == Vector4.zero ? level : S3.Ortho(Vector4.Lerp(kept, level.normalized * steady, steady), lowPos, look);
        if (up == Vector4.zero) up = S3.Ortho(S3.EY, lowPos, look);
        Matrix4x4 low = S3.FrameAt(lowPos, up, look);
        if (high <= 0.001f) return low;

        TurnBetween(low, trailing, out Vector3 axis, out float degrees);
        return Blend(low, trailing.GetColumn(3), high, axis, degrees);
    }

    /// <summary>
    /// Trail the ball on a leash, looking straight at it so it stays centred (ed3vv's flight camera).
    /// "Up" points away from the nearest ground, so crossing the pole rolls the view over.
    /// </summary>
    void TrackBall(float dt)
    {
        float d = S3.Dist(camPos, ball.pos) * R;
        if (d > leash) camPos = S3.Walk(camPos, S3.DirTo(camPos, ball.pos), (d - leash) / R);
        if (Mathf.Asin(Mathf.Clamp(camPos.y, -1f, 1f)) * R < followMinHeight)
            camPos = S3.Lift(S3.Foot(camPos), followMinHeight / R);

        float k = 1 - Mathf.Exp(-lookRate * dt);
        Vector4 look = S3.DirTo(camPos, ball.pos);
        Vector4 f = look == Vector4.zero ? S3.Ortho(camFwd, camPos) : look;
        if (f != Vector4.zero) camFwd = f;

        Vector4 prevUp = S3.Ortho(camUp, camPos, camFwd);
        if (prevUp == Vector4.zero) prevUp = S3.FrameAt(camPos, camUp, camFwd).GetColumn(1);
        Vector4 away = S3.EY - camPos.y * camPos;
        Vector4 target = away.magnitude > 1e-4f ? S3.Ortho(away, camPos, camFwd) : Vector4.zero;
        if (target == Vector4.zero) target = prevUp;
        // rotate about the view axis towards target (handles a full 180° roll)
        float angle = Mathf.Acos(Mathf.Clamp(Vector4.Dot(prevUp, target), -1f, 1f));
        Vector4 side = S3.Ortho(target, camPos, camFwd, prevUp);
        if (side == Vector4.zero) side = S3.FrameAt(camPos, prevUp, camFwd).GetColumn(0);
        float turn = angle * k;
        camUp = Mathf.Cos(turn) * prevUp + Mathf.Sin(turn) * side;
    }

    // ------------------------------------------------------------------ gliding to the ball

    /// <summary>Glide to the ball shortly after it stops, unless you've stepped away (Tab) in the meantime.</summary>
    void QueueGlide()
    {
        if (!exploring) return;
        glidePending = true;
        glideWait = glideDelay;
    }

    /// <summary>Fly the camera from wherever it is now to the address view behind the ball.</summary>
    void GlideToBall()
    {
        glideFrom = player.ViewFrame();
        followWeight = 0;
        powerSelecting = false;
        exploring = false;
        player.inputEnabled = false;
        glidePending = false;
        Matrix4x4 to = BallCamera();
        glideLength = S3.Dist(glideFrom.GetColumn(3), to.GetColumn(3)) * R;
        TurnBetween(glideFrom, to, out glideAxis, out glideDegrees);
        glideTime = Mathf.Clamp(glideMinTime + glideSecondsPerMetre * glideLength, glideMinTime, glideMaxTime);
        glideT = 0;
        gliding = glideLength > 0.05f;
    }

    void UpdateGlide(float dt)
    {
        if (glidePending && state == State.Ready && exploring)
        {
            glideWait -= dt;
            if (glideWait <= 0)
            {
                aimDir = viewDir = RoughDirectionToCup();
                lookPitch = 0;
                GlideToBall();
            }
        }
        if (gliding && (glideT += dt) >= glideTime) gliding = false;
    }

    /// <summary>The view part-way along the glide: eased, arcing up over obstacles.</summary>
    Matrix4x4 GlideFrame(Matrix4x4 target)
    {
        float s = Mathf.SmoothStep(0, 1, Mathf.Clamp01(glideT / glideTime));
        Matrix4x4 m = Blend(glideFrom, target.GetColumn(3), s, glideAxis, glideDegrees);
        float arc = Mathf.Min(glideArc * glideLength, glideMaxArc) * Mathf.Sin(Mathf.PI * s);
        Vector4 pos = m.GetColumn(3);
        Vector4 up = S3.EY - pos.y * pos;
        if (arc > 0.01f && up.magnitude > 1e-4f) m = S3.PlaneRotation(pos, up.normalized, arc / R) * m;
        return m;
    }

    /// <summary>Frame f carried t of the way along the geodesic to point q (parallel transport).</summary>
    static Matrix4x4 Carry(Matrix4x4 f, Vector4 q, float t)
    {
        Vector4 p = f.GetColumn(3), d = S3.DirTo(p, q);
        float angle = S3.Dist(p, q);
        return angle > 1e-5f && d != Vector4.zero ? S3.PlaneRotation(p, d, t * angle) * f : f;
    }

    /// <summary>
    /// The turn from a's view to b's once b is carried back to a's position. Carried along the
    /// geodesic, it's the same at every point of the glide, so it's worked out once: recomputing
    /// it each frame lets a turn of nearly 180° flip between turning left and right.
    /// </summary>
    static void TurnBetween(Matrix4x4 a, Matrix4x4 b, out Vector3 axis, out float degrees)
    {
        Matrix4x4 rel = a.transpose * Carry(b, a.GetColumn(3), 1);   // b's axes in a's basis
        Vector4 f = rel.GetColumn(2), u = rel.GetColumn(1);
        Quaternion.LookRotation(new Vector3(f.x, f.y, f.z), new Vector3(u.x, u.y, u.z)).ToAngleAxis(out degrees, out axis);
        if (degrees > 180) { degrees = 360 - degrees; axis = -axis; }
        if (!(axis.sqrMagnitude > 0.5f)) { axis = Vector3.up; degrees = 0; }
    }

    /// <summary>The view s of the way from a to position b: a carried along the geodesic, turned s of the way.</summary>
    static Matrix4x4 Blend(Matrix4x4 a, Vector4 b, float s, Vector3 axis, float degrees)
    {
        Matrix4x4 m = Carry(a, b, s) * Matrix4x4.Rotate(Quaternion.AngleAxis(s * degrees, axis));
        return S3.FrameAt(m.GetColumn(3), m.GetColumn(1), m.GetColumn(2));
    }

    // ------------------------------------------------------------------ visuals

    void UpdateVisuals()
    {
        bool sinking = state == State.Sunk;
        ballRenderer.enabled = (!sinking || sinkTime < BurstAt + 0.1f) && state != State.Finished;
        S3World.SetFrame(ballRenderer, sinking ? SinkFrame() : ball.frame);
        // the ball has stopped: it flashes, and a ring closes in on it and stays while you walk over
        S3World.SetLook(ballRenderer, 1.2f * Mathf.Clamp01(1 - stopTime / 0.8f), 0, Color.white);
        restRenderer.enabled = state == State.Ready && exploring;
        if (restRenderer.enabled)
        {
            float u = Mathf.Clamp01(stopTime / 0.55f), settle = 1 - (1 - u) * (1 - u) * (1 - u);
            float r = Mathf.Lerp(2.6f, ballRadius + 0.32f, settle) + 0.04f * u * Mathf.Sin(Time.time * 3.5f);
            S3Scenery.SetRing(restMesh, r, Mathf.Lerp(0.22f, 0.07f, settle));
            S3World.SetFrame(restRenderer, S3.FrameAt(ball.Foot, S3.EY, aimDir));
        }
        bool aiming = state == State.Ready && !exploring;
        UpdateAntipodes(aiming);
        Vector4 d = ShotDirection;
        arrowRenderer.enabled = guideRenderer.enabled = aiming && d != Vector4.zero;
        guideRenderer.enabled &= Overhead < 0.3f;   // standing over the ball, the camera would look up through the guide's dots
        if (!arrowRenderer.enabled) return;
        S3World.SetFrame(arrowRenderer, S3.FrameAt(ball.Foot, S3.EY, d));
        S3World.SetFrame(guideRenderer, S3.FrameAt(ball.pos, S3.UpAt(ball.pos), d) *
                                        S3.PlaneRotation(S3.EZ, S3.EY, aimAngle * Mathf.Deg2Rad));
    }

    /// <summary>How strongly to show where a straight-up shot would come down (fades in as you aim steeply).</summary>
    float Steepness => Mathf.InverseLerp(58f, 78f, aimAngle);

    /// <summary>
    /// The beacon at the ball's antipode while you aim steeply or a steep shot is in the air: where a ball hit
    /// straight over the pole comes down.
    /// </summary>
    void UpdateAntipodes(bool aiming)
    {
        bool playing = state != State.Sunk && state != State.Finished;
        if (!beaconRenderer) return;
        float show = aiming ? Steepness : state == State.Moving && steepShot ? 1 : 0;
        beaconRenderer.enabled = playing && show > 0.01f;
        if (!beaconRenderer.enabled) return;
        Vector4 at = aiming ? -ball.Foot : -lastShot;
        S3World.SetFrame(beaconRenderer, S3.FrameAt(at, S3.EY, S3.Ortho(S3.EZ, at, S3.EY) == Vector4.zero ? S3.EX : S3.EZ));
        SetGlow(beaconRenderer, show * (1.2f + 0.25f * Mathf.Sin(Time.time * 3f)), 0.4f);
    }

    void SetGlow(Renderer r, float intensity, float shimmer)
    {
        glowBlock ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(glowBlock);
        glowBlock.SetFloat("_Intensity", intensity);
        glowBlock.SetFloat("_Shimmer", shimmer);
        r.SetPropertyBlock(glowBlock);
    }

    /// <summary>The centre banner; celebrate makes it the gold one.</summary>
    void Say(string text, float seconds, bool celebrate = false) => hud?.Banner(text, seconds, celebrate);

    /// <summary>Meter position whose shot just carries straight up over the pole.</summary>
    float PoleCrossingPower()
    {
        float need = ball.PoleSpeed(ballRadius) * 1.02f;
        float lo = 0, hi = 1;
        if (LaunchSpeed(1, R) < need) return 2;
        for (int i = 0; i < 30; i++) { float m = (lo + hi) / 2; if (LaunchSpeed(m, R) < need) lo = m; else hi = m; }
        return hi;
    }

    // ------------------------------------------------------------------ capture / test hooks

    /// <summary>Show the aim view at a given launch height without input.</summary>
    public void DebugAim(float angleDeg)
    {
        StartHole(holeIndex);
        aimAngle = angleDeg;
        UpdateVisuals();
        UpdateSeeThrough(10f);
    }

    /// <summary>Restart hole 1 and fly a shot for the given time with fixed steps.</summary>
    public void DebugFly(float shotPower, float angleDeg, float seconds)
    {
        StartHole(0);
        aimAngle = angleDeg;
        Shoot(shotPower);
        for (float t = 0; t < seconds; t += 1 / 60f)
        {
            if (state == State.Moving) Simulate(1 / 60f);
            UpdateFollow(1 / 60f);
        }
        UpdateVisuals();
        UpdateSeeThrough(10f);
    }

    /// <summary>Put the ball (at rest) at a ground point, aiming at the cup.</summary>
    public void DebugPlaceBall(Vector4 groundPoint)
    {
        ball.Place(groundPoint);
        state = State.Ready;
        aimDir = viewDir = DirectionToCup();
        UpdateVisuals();
        UpdateSeeThrough(10f);
    }

    public S3Puzzle Puzzle => puzzle;
    public void DebugStartPuzzle(int index) => StartPuzzle(index);
    public void DebugStartHole(int index) { lockHole = -1; StartHole(index); }

    public string DebugState =>
        $"state {state}, ball height {ball.HeightM:0.0} m, ground distance from tee {world.GroundDistance(ball.Foot, Hole.tee):0.0} m, follow {followWeight:0.00}";

    // ------------------------------------------------------------------ HUD (S3HudView, UI Toolkit)

    readonly List<(string, string)> hudChips = new List<(string, string)>();
    readonly List<(string, string)> hudHints = new List<(string, string)>();

    /// <summary>Pushes this frame's state to the HUD (after the camera has moved, so markers line up).</summary>
    void LateUpdate()
    {
        if (world == null || hud == null || (ball == null && puzzle == null)) return;
        bool replaying = replay != null && replay.Active && !titleUp;
        hud.SetReplay(replaying, replaying ? ReplayLine() : null, replaying ? replay.Progress : 0);
        bool covered = replaying || titleUp || MenuOpen || hud.Current == S3HudView.Menu.Results || hud.Current == S3HudView.Menu.HoleDone;
        hud.SetHudVisible(!covered);
        if (covered) return;
        bool mouseFree = Cursor.lockState != CursorLockMode.Locked;

        hud.BeginMarkers();
        if (!MenuOpen) Waypoints();
        hud.EndMarkers();

        hudChips.Clear();
        hudHints.Clear();
        string note = null;
        if (ball == null)
        {
            // the Desert: puzzles on foot
            hudChips.Add((puzzle.Level?.title ?? "", null));
            hud.SetScorecard(world.MapName, $"Puzzle {puzzleIndex + 1}", $"/ {world.levels.Count}", world.levels.Count, puzzleIndex, hudChips, null);
            hud.SetStats(-1, -1);
            hud.SetPower(false, 0, -1, -1);
            PuzzleHud();
            if (!puzzle.Solved) { hudHints.Add(("LMB", "Place")); hudHints.Add(("Q E", "Turn")); hudHints.Add(("H", HintVisible ? "Hide hint" : "Hint")); }
        }
        else
        {
            bool aiming = state == State.Ready && !exploring;
            hudChips.Add(("Par", $"{Hole.par}"));
            hudChips.Add(("Stroke", $"{strokes}"));
            hudChips.Add(("Total", $"{total}"));
            hud.SetScorecard(world.MapName, $"Hole {holeIndex + 1}", $"/ {world.holes.Count}", world.holes.Count, holeIndex, hudChips,
                             locked ? $"Cup sealed  ·  light {TheCrystal}" : null);
            hud.SetStats(world.GroundDistance(ball.Foot, Hole.cup), aiming ? aimAngle : -1);
            if (puzzle != null) PuzzleHud();
            else { hud.SetCrosshair(false, false); hud.SetInventory(false, 0, 0, 0, 0, true); hud.SetStatus(null); hud.SetHintCard(false, null, null); }
            string key = puzzle == null ? "E" : "F";
            if (gliding || glidePending) { }
            else if (aiming && !powerSelecting && world.GroundDistance(ball.Foot, -Hole.cup) < world.greenRadius)
                note = "On the flag's antipode: hit it straight up at full power and it comes down on the green";
            else if (aiming && powerSelecting) { hudHints.Add(("Space", "Shoot")); hudHints.Add(("RMB", "Cancel")); }
            else if (aiming) { hudHints.Add(("A D", "Aim")); hudHints.Add(("W S", "Height")); hudHints.Add(("Space", "Power")); hudHints.Add(("Tab", "Walk")); }
            else if (state == State.Ready)
            {
                hudHints.Add((key, NearBall ? "Address ball" : "Back to ball"));
                if (locked) { hudHints.Add(("LMB", "Place")); hudHints.Add(("Q E", "Turn")); hudHints.Add(("H", HintVisible ? "Hide hint" : "Hint")); }
            }
            hud.SetPower(powerSelecting, power, aimAngle > 75 ? PoleCrossingPower() : -1, lastPower);
        }
        hud.SetPrompt(mouseFree && !MenuOpen);
        hud.SetHints(note, hudHints);
    }

    /// <summary>
    /// Waypoints: the crystals still dark, the flag (always), the antipode while aiming steeply, and your
    /// ball whenever you're not addressing it, each with its distance from you.
    /// </summary>
    void Waypoints()
    {
        if (state == State.Finished) return;
        if (puzzle != null && (ball == null || locked) && !puzzle.Solved)
            foreach (var goal in puzzle.UnlitGoals)
                hud.Marker(S3.Lift(goal.position, 2.1f / R), S3HudView.MarkerKind.Gem, $"{world.GroundDistance(player.Foot, goal.position):0} m");
        if (ball == null) return;
        hud.Marker(S3.Lift(Hole.cup, 2.9f / R), S3HudView.MarkerKind.Cup, $"{world.GroundDistance(player.ViewFrame().GetColumn(3), Hole.cup):0} m");
        if (state == State.Ready && !exploring && Steepness > 0)
            hud.Marker(S3.Lift(-ball.Foot, 2.2f / R), S3HudView.MarkerKind.Pole,
                       $"Over the pole  ·  {world.GroundDistance(-ball.Foot, Hole.cup):0} m from the pin", Steepness);
        if (exploring && state != State.Sunk && followWeight < 0.5f && !gliding)   // no marker on the ball the camera is following
        {
            // the ball's marker says what it's doing, and pops when it comes to rest
            float d = world.GroundDistance(player.Foot, ball.Foot);
            bool resting = state == State.Ready;
            string label = !resting ? (ball.grounded ? "Rolling" : "In the air") : stopTime < 2.5f ? $"Stopped  ·  {d:0} m" : $"{d:0} m";
            float pop = resting ? 1 + 0.6f * Mathf.Clamp01(1 - stopTime / 0.45f) : 0.85f;
            hud.Marker(S3.Lift(ball.Foot, (ballRadius * 2 + 0.5f) / R), S3HudView.MarkerKind.Ball, label, 1, pop);
        }
    }

    bool HintVisible => puzzle != null && showHint && Time.realtimeSinceStartup < hintUntil && puzzle.Level != null && (ball == null || locked);

    /// <summary>Crosshair, pieces in hand, puzzle status and the level's hint.</summary>
    void PuzzleHud()
    {
        bool onFoot = exploring && Cursor.lockState == CursorLockMode.Locked;
        hud.SetCrosshair(onFoot && (ball == null || state != State.Finished), puzzle.HasTarget);
        bool relevant = ball == null || locked || exploring;
        var level = puzzle.Level;
        bool pieces = relevant && level != null && (level.mirrors > 0 || level.diffractors > 0) && (ball == null || exploring);
        hud.SetInventory(pieces, level?.mirrors ?? 0, puzzle.mirrorsLeft, level?.diffractors ?? 0, puzzle.diffractorsLeft, puzzle.tool == S3Laser.Kind.Mirror);
        hud.SetStatus(pieces ? puzzle.StatusText : null);
        hud.SetHintCard(HintVisible, level?.title, level?.hint);
    }
}
