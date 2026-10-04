using System;
using System.Collections.Generic;
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
/// and L opens the level selector to jump straight to any hole or puzzle.
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
    int holeIndex, strokes, total;
    bool powerSelecting, exploring;
    float power, powerPhase, accumulator, moveTime, sunkTimer, aimAngle;
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
    string message = "";
    float messageUntil;
    Camera cam;
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
    // level selector (L)
    bool selecting;
    string[][] levelLabels;       // per map in Maps order
    static readonly S3World.Biome[] Maps = { S3World.Biome.Meadow, S3World.Biome.Desert, S3World.Biome.Basin };

    S3World.Hole Hole => world.holes[holeIndex];
    float R => world.radius;

    void Start() => Init();

    /// <summary>Creates the ball, flag and aim helpers and starts hole 1 (also used by the capture tool).</summary>
    public void Init()
    {
        world = S3World.Instance;
        if (!player) player = FindAnyObjectByType<S3Player>();
        cam = player.GetComponent<Camera>();
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
        ball = NewBall(R);
        ball.radius = ballRadius;
        ConfigureBall(ball);
        ballRenderer = world.CreateRenderer("Golf Ball", S3Scenery.BallMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        flagRenderer = world.CreateRenderer("Flag", S3Scenery.FlagMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        arrowRenderer = world.CreateRenderer("Aim Arrow", S3Scenery.ArrowMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        guideRenderer = world.CreateRenderer("Launch Guide", S3Scenery.GuideMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        restMesh = S3Scenery.RingMesh(48, new Color(0.25f, 0.9f, 1f, 0.4f));
        restRenderer = world.CreateRenderer("Rest Ring", restMesh, world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
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
        ShowHint(14f);
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
        Say($"{(Crystals > 1 ? "Crystals" : "Crystal")} lit!\nThe cup is open.", 3.5f);
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

    /// <summary>Opens or closes the level selector; the game (and the walker) is paused while it's up.</summary>
    void SetSelecting(bool on)
    {
        selecting = on;
        player.paused = on;
        if (!on) return;
        powerSelecting = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        levelLabels ??= BuildLevelLabels();
    }

    string[][] BuildLevelLabels()
    {
        string Hole(S3World.HoleDef h, int i)
        {
            float d = S3.Dist(S3.GroundPoint(h.tee.x, h.tee.y, R), S3.GroundPoint(h.cup.x, h.cup.y, R)) * R;
            return $"Hole {i + 1}  ·  Par {h.par}  ·  {d:0} m";
        }
        var meadow = new string[world.course.Length];
        for (int i = 0; i < meadow.Length; i++) meadow[i] = Hole(world.course[i], i);
        var desert = new string[S3Levels.DesertOrder.Length];
        for (int i = 0; i < desert.Length; i++)
            desert[i] = $"Puzzle {i + 1}  ·  {S3Levels.Build(S3Levels.DesertOrder[i], Matrix4x4.identity, R).title}";
        var basin = new string[world.basinCourse.Length];
        for (int i = 0; i < basin.Length; i++)
            basin[i] = $"Hole {i + 1}  ·  Par {world.basinCourse[i].par}" +
                       (i < world.basinLocks.Length ? $"  ·  {world.BuildLock(world.basinLocks[i]).title}" : "");
        return new[] { meadow, desert, basin };
    }

    /// <summary>Start a given hole (or Desert puzzle) on a map, from scratch.</summary>
    void JumpTo(S3World.Biome b, int index)
    {
        SetSelecting(false);
        if (b != world.biome) { world.SetBiome(b); Init(); }   // rebuilding clears the old map's renderers
        advancing = false;
        if (world.HasGolf) { total = 0; lockHole = -1; StartHole(index); }
        else StartPuzzle(index);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
            Say(last ? "ALL PUZZLES SOLVED\nNext: the Eternal Poles" : "CRYSTAL LIT!", 3.5f);
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
        S3World.SetFrame(flagRenderer, S3.FrameAt(Hole.cup, S3.EY, toTee == Vector4.zero ? S3.EZ : toTee));
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
        if (locked) ShowHint(10f);
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

    void Update()
    {
        if (world == null) return;
        var keys = Keyboard.current;
        if (keys != null && (keys.lKey.wasPressedThisFrame || (selecting && keys.escapeKey.wasPressedThisFrame)))
            SetSelecting(!selecting);
        if (selecting) return;
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
                    aimDir = viewDir = facing != Vector4.zero ? facing : DirectionToCup();
                    lookPitch = 0;
                    GlideToBall();
                    S3Audio.Click();
                }
            }
            if (kb.mKey.wasPressedThisFrame) { GoToMap(NextMap(world.biome)); return; }
            if (kb.rKey.wasPressedThisFrame && state != State.Sunk && state != State.Finished) { total -= strokes; StartHole(holeIndex); }
            if (kb.nKey.wasPressedThisFrame) { total = 0; lockHole = -1; advancing = false; StartHole(0); }
            if (kb.hKey.wasPressedThisFrame && puzzle != null) { if (showHint && Time.realtimeSinceStartup < hintUntil) showHint = false; else ShowHint(1e6f); }
            if (kb.enterKey.wasPressedThisFrame && advancing) advanceTimer = 0;
            if (kb.bKey.wasPressedThisFrame && (state == State.Ready || state == State.Moving) && strokes > 0)
            {
                ball.Place(lastShot);
                state = State.Ready;
                followWeight = 0;
                powerSelecting = false;
                strokes++; total++;
                aimDir = viewDir = DirectionToCup();
                lookPitch = 0;
                GlideToBall();
                Say("Ball returned (+1 stroke)", 2.5f);
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
            lookPitch = Mathf.Clamp(lookPitch + md.y * player.lookSensitivity, -1.45f - CameraPitch(), 1.45f - CameraPitch());
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
                sunkTimer -= dt;
                if (sunkTimer <= 0)
                {
                    if (holeIndex + 1 < world.holes.Count) StartHole(holeIndex + 1);
                    else
                    {
                        state = State.Finished;
                        int par = 0;
                        foreach (var h in world.holes) par += h.par;
                        if (world.biome == S3World.Biome.Meadow)
                        {
                            Say($"Meadow complete!\n{total} strokes (par {par})  ·  next: the Desert", 6f);
                            AdvanceIn(6f);
                        }
                        else Say((world.biome == S3World.Biome.Basin ? "Campaign complete!" : $"{world.MapName} complete!") +
                                 $"\n{total} strokes (par {par})  ·  N play again  ·  L choose a level", 1e6f);
                    }
                }
                break;
        }
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
                sunkTimer = 3f;
                int diff = strokes - Hole.par;
                string name = strokes == 1 ? "HOLE IN ONE!" :
                    diff <= -2 ? "Eagle!" : diff == -1 ? "Birdie!" : diff == 0 ? "Par" : diff == 1 ? "Bogey" : $"+{diff}";
                Say($"{name}\nSunk in {strokes}", 3f);
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
                    Say("Over the pole!\nIt came down on the far side of the world.", 3.5f);
                return;
            }
        }
        if (moveTime > 40f) { ball.vel = Vector4.zero; state = State.Ready; stopTime = 0; QueueGlide(); }
    }

    // ------------------------------------------------------------------ cameras

    /// <summary>ed3vv's pitch rule: tilt up as the launch height rises, keeping the ball in view.</summary>
    float CameraPitch() => Mathf.Clamp(-0.30f + 0.26f * (aimAngle - 24f) * Mathf.Deg2Rad, -0.85f, 0.42f);

    /// <summary>The standing golfer view: behind the ball along viewDir, looking past it.</summary>
    Matrix4x4 BallCamera()
    {
        Vector4 foot = ball.Foot;
        if (state == State.Sunk || state == State.Finished) foot = Hole.cup;
        Vector4 dir = S3.Ortho(viewDir, foot, S3.EY);
        if (dir == Vector4.zero) dir = DirectionToCup();
        float back = cameraBack / R;
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
                aimDir = viewDir = DirectionToCup();
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
        ballRenderer.enabled = state != State.Sunk && state != State.Finished;
        S3World.SetFrame(ballRenderer, ball.frame);
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

    void Say(string text, float seconds)
    {
        message = text;
        messageUntil = Time.realtimeSinceStartup + seconds;
    }

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

    // ------------------------------------------------------------------ HUD (drawn with S3Hud)

    void OnGUI()
    {
        if (world == null || (ball == null && puzzle == null)) return;
        S3Hud.Begin();
        float w = S3Hud.Width, h = S3Hud.Height;
        if (selecting) { DrawLevelSelect(w, h); return; }

        DrawWaypoint();
        bool mouseFree = Cursor.lockState != CursorLockMode.Locked;
        var hints = new List<S3Hud.Hint>();
        string note = mouseFree ? "Click to play" : null;
        if (ball == null)
        {
            // the Desert: puzzles on foot
            DrawScorecard(world.MapName, $"Puzzle {puzzleIndex + 1} / {world.levels.Count}", puzzle.Level?.title);
            DrawPuzzleHud(w, h);
            if (!puzzle.Solved) { hints.Add(new S3Hud.Hint("LMB", "Place")); hints.Add(new S3Hud.Hint("Q E", "Turn")); hints.Add(new S3Hud.Hint("H", "Hint")); }
        }
        else
        {
            bool aiming = state == State.Ready && !exploring;
            DrawScorecard(world.MapName, $"Hole {holeIndex + 1} / {world.holes.Count}", $"Par {Hole.par}   ·   Stroke {strokes}   ·   Total {total}", locked);
            DrawStats(w, aiming);
            if (puzzle != null) DrawPuzzleHud(w, h);
            string key = puzzle == null ? "E" : "F";
            if (note != null || gliding || glidePending) { }
            else if (aiming && !powerSelecting && world.GroundDistance(ball.Foot, -Hole.cup) < world.greenRadius)
                note = "On the flag's antipode: hit it straight up at full power and it comes down on the green";
            else if (aiming && powerSelecting) { hints.Add(new S3Hud.Hint("Space", "Shoot")); hints.Add(new S3Hud.Hint("RMB", "Cancel")); }
            else if (aiming) { hints.Add(new S3Hud.Hint("A D", "Aim")); hints.Add(new S3Hud.Hint("W S", "Height")); hints.Add(new S3Hud.Hint("Space", "Power")); }
            else if (state == State.Ready)
            {
                hints.Add(new S3Hud.Hint(key, NearBall ? "Address ball" : "Back to ball"));
                if (locked) { hints.Add(new S3Hud.Hint("LMB", "Place")); hints.Add(new S3Hud.Hint("Q E", "Turn")); }
            }
            if (powerSelecting) DrawPowerMeter(w, h);
        }
        if (note != null) S3Hud.Note(w / 2, h - 46, note);
        else S3Hud.Hints(w / 2, h - 46, hints);
        DrawFooter(h);
        DrawMessage(w, MessageY(h));
    }

    /// <summary>Top left: the map, the hole (or puzzle), and the score.</summary>
    void DrawScorecard(string map, string title, string line, bool sealedCup = false)
    {
        const float x = 24, y = 24, pad = 20;
        bool hasLine = !string.IsNullOrEmpty(line);
        float textW = Mathf.Max(S3Hud.Measure(title, S3Hud.Heading).x, hasLine ? S3Hud.Measure(line, S3Hud.Body).x : 0);
        float cw = Mathf.Max(230, textW + 2 * pad), ch = hasLine ? 108 : 80;
        S3Hud.Panel(new Rect(x, y, cw, ch));
        S3Hud.Text(new Rect(x + pad, y + 16, cw, 16), map.ToUpperInvariant(), S3Hud.Eyebrow, S3Hud.Accent, false);
        S3Hud.Text(new Rect(x + pad, y + 34, cw, 36), title, S3Hud.Heading, S3Hud.Ink, false);
        if (hasLine) S3Hud.Text(new Rect(x + pad, y + 74, cw, 22), line, S3Hud.Body, S3Hud.Dim, false);
        if (!sealedCup) return;
        string s = $"Cup sealed  ·  light {TheCrystal}";
        float sw = S3Hud.Measure(s, S3Hud.Small).x + 32;
        var r = new Rect(x, y + ch + 10, sw, 32);
        S3Hud.Pill(r, new Color(0.22f, 0.45f, 0.70f, 0.8f));
        S3Hud.Text(new Rect(r.x + 16, r.y + 7, sw, 18), s, S3Hud.Small, S3Hud.Ink, false);
    }

    /// <summary>Top right: distance to the pin, and the launch angle while you aim.</summary>
    void DrawStats(float w, bool aiming)
    {
        const float colW = 108, pad = 20;
        int n = aiming ? 2 : 1;
        var r = new Rect(w - 24 - (n * colW + pad), 24, n * colW + pad, 80);
        S3Hud.Panel(r);
        void Stat(int i, string label, string value)
        {
            float x = r.x + pad + i * colW;
            S3Hud.Text(new Rect(x, r.y + 16, colW, 16), label, S3Hud.Eyebrow, S3Hud.Dim, false);
            S3Hud.Text(new Rect(x, r.y + 34, colW, 36), value, S3Hud.Heading, S3Hud.Ink, false);
        }
        Stat(0, "PIN", $"{world.GroundDistance(ball.Foot, Hole.cup):0} m");
        if (aiming) Stat(1, "LAUNCH", $"{aimAngle:0}°");
    }

    void DrawPowerMeter(float w, float h)
    {
        const float bw = 400, bh = 14;
        // just a slim bar and its labels, no card: steep shots tilt the view up and put the ball down here
        float x = w / 2 - bw / 2, y = h - 100;
        S3Hud.Text(new Rect(x, y - 24, bw, 16), "POWER", S3Hud.Eyebrow, S3Hud.Ink);
        string pct = $"{power * 100:0}%";
        float pw = S3Hud.Measure(pct, S3Hud.BodyBold).x;
        S3Hud.Text(new Rect(x + bw - pw, y - 27, pw + 2, 22), pct, S3Hud.BodyBold, S3Hud.Ink);
        S3Hud.Bar(new Rect(x - 3, y - 3, bw + 6, bh + 6), new Color(0.06f, 0.08f, 0.12f, 0.7f));
        S3Hud.Bar(new Rect(x, y, bw, bh), new Color(1, 1, 1, 0.12f));
        Color mid = new Color(1f, 0.82f, 0.30f);
        Color fill = power < 0.5f ? Color.Lerp(new Color(0.36f, 0.86f, 0.46f), mid, power * 2) : Color.Lerp(mid, new Color(1f, 0.34f, 0.26f), power * 2 - 1);
        if (power > 0.005f) S3Hud.Bar(new Rect(x, y, Mathf.Max(bh, bw * power), bh), fill);
        float poleMark = PoleCrossingPower();
        if (aimAngle > 75 && poleMark <= 1)
        {
            float px = x + bw * poleMark;
            GUI.color = new Color(1, 1, 1, 0.9f);
            GUI.DrawTexture(new Rect(px - 1, y - 5, 2, bh + 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            S3Hud.Centered(px, y + bh + 14, "OVER THE POLE", S3Hud.Eyebrow, S3Hud.Ink, true);
        }
    }

    /// <summary>The one permanent reminder: everything else lives in the L menu.</summary>
    void DrawFooter(float h)
    {
        float kw = S3Hud.Key(24, h - 36, "L");
        S3Hud.Text(new Rect(24 + kw + 10, h - 47, 300, 22), "Levels & controls", S3Hud.Small, S3Hud.Dim);
    }

    /// <summary>The centre message, on its own card: the first line big, the rest smaller underneath. It fades out at the end.</summary>
    void DrawMessage(float w, float y)
    {
        float left = messageUntil - Time.realtimeSinceStartup;
        if (left <= 0 || message.Length == 0) return;
        var lines = message.Split('\n');
        float textW = S3Hud.Measure(lines[0], S3Hud.Title).x;
        for (int i = 1; i < lines.Length; i++) textW = Mathf.Max(textW, S3Hud.Measure(lines[i], S3Hud.Sub).x);
        float cw = Mathf.Min(w - 48, textW + 80), ch = 80 + 30 * (lines.Length - 1) + (lines.Length > 1 ? 12 : 0);
        var c = GUI.color;
        GUI.color = new Color(1, 1, 1, Mathf.Clamp01(left / 0.35f));
        S3Hud.Panel(new Rect(w / 2 - cw / 2, y, cw, ch), 1.3f);
        S3Hud.Text(new Rect(0, y + 12, w, 56), lines[0], S3Hud.Title, S3Hud.Ink, false);
        for (int i = 1; i < lines.Length; i++)
            S3Hud.Text(new Rect(0, y + 68 + 30 * (i - 1), w, 28), lines[i], S3Hud.Sub, S3Hud.Dim, false);
        GUI.color = c;
    }

    /// <summary>
    /// Waypoints: the flag (always) and your ball (whenever you're not addressing it), each with
    /// its distance from you. Targets are projected exactly like the shader projects geometry;
    /// off screen (or behind you) a marker sticks to the screen edge, pointing the way.
    /// </summary>
    void DrawWaypoint()
    {
        if (state == State.Finished) return;
        // the crystals still to light (Desert, and a sealed Eternal Poles cup)
        if (puzzle != null && (ball == null || locked) && !puzzle.Solved)
            foreach (var goal in puzzle.UnlitGoals)
            {
                Vector4 g = goal.position;
                DrawMarker(S3.Lift(g, 2.1f / R), new Color(0.80f, 0.64f, 1f), $"{world.GroundDistance(player.Foot, g):0} m", S3Hud.Diamond);
            }
        if (ball == null) return;
        DrawMarker(S3.Lift(Hole.cup, 2.9f / R), S3Hud.Accent, $"{world.GroundDistance(player.ViewFrame().GetColumn(3), Hole.cup):0} m", S3Hud.Triangle);
        if (state == State.Ready && !exploring && Steepness > 0)
        {
            var c = new Color(S3Hud.Ice.r, S3Hud.Ice.g, S3Hud.Ice.b, Steepness);
            DrawMarker(S3.Lift(-ball.Foot, 2.2f / R), c, $"Over the pole  ·  {world.GroundDistance(-ball.Foot, Hole.cup):0} m from the pin", S3Hud.Ring);
        }
        if (exploring && state != State.Sunk && followWeight < 0.5f && !gliding)   // no marker on the ball the camera is following
        {
            // the ball's marker says what it's doing, and pops when it comes to rest
            float d = world.GroundDistance(player.Foot, ball.Foot);
            bool resting = state == State.Ready;
            string label = !resting ? (ball.grounded ? "Rolling" : "In the air") : stopTime < 2.5f ? $"Stopped  ·  {d:0} m" : $"{d:0} m";
            float pop = resting ? 1 + 0.6f * Mathf.Clamp01(1 - stopTime / 0.45f) : 0.85f;
            DrawMarker(S3.Lift(ball.Foot, (ballRadius * 2 + 0.5f) / R), new Color(0.55f, 0.95f, 1f), label, S3Hud.Circle, pop);
        }
    }

    float hintBottom;   // where the hint card ended last frame (virtual pixels)

    bool HintVisible =>puzzle != null && showHint && Time.realtimeSinceStartup < hintUntil && puzzle.Level != null && (ball == null || locked);

    /// <summary>Big centre messages sit below the hint card while it's up.</summary>
    float MessageY(float h) => HintVisible ? Mathf.Max(h * 0.24f, hintBottom + 20) : h * 0.24f;

    /// <summary>Crosshair, pieces in hand, puzzle status and the level's hint.</summary>
    void DrawPuzzleHud(float w, float h)
    {
        bool onFoot = exploring && Cursor.lockState == CursorLockMode.Locked;
        if (onFoot && (ball == null || state != State.Finished))
        {
            Color c = puzzle.HasTarget ? S3Hud.Accent : new Color(1, 1, 1, 0.85f);
            S3Hud.Icon(new Rect(w / 2 - 10, h / 2 - 10, 20, 20), S3Hud.Ring, c);
            S3Hud.Icon(new Rect(w / 2 - 2.5f, h / 2 - 2.5f, 5, 5), S3Hud.Circle, c, false);
        }
        bool relevant = ball == null || locked || exploring;
        if (relevant && puzzle.Level != null && (puzzle.Level.mirrors > 0 || puzzle.Level.diffractors > 0) && (ball == null || exploring))
        {
            DrawInventory(w / 2, h - 106);
            S3Hud.Note(w / 2, h - 160, puzzle.StatusText, null, S3Hud.Small);
        }
        if (HintVisible) DrawLevelHint(w);
    }

    /// <summary>The pieces you have, as chips; the one in hand is highlighted.</summary>
    void DrawInventory(float cx, float cy)
    {
        var items = new List<(string key, string name, int count, bool held)>();
        if (puzzle.Level.mirrors > 0) items.Add(("1", "Mirror", puzzle.mirrorsLeft, puzzle.tool == S3Laser.Kind.Mirror));
        if (puzzle.Level.diffractors > 0) items.Add(("2", "Diffractor", puzzle.diffractorsLeft, puzzle.tool == S3Laser.Kind.Diffractor));
        const float gap = 10, pad = 14, height = 44;
        float Width((string key, string name, int count, bool held) it) =>
            pad + 24 + 10 + S3Hud.Measure(it.name, S3Hud.Body).x + 10 + S3Hud.Measure($"×{it.count}", S3Hud.BodyBold).x + pad;
        float total = -gap;
        foreach (var it in items) total += Width(it) + gap;
        float x = cx - total / 2;
        foreach (var it in items)
        {
            float cw = Width(it);
            var r = new Rect(x, cy - height / 2, cw, height);
            if (it.held) S3Hud.Panel(r, new Color(S3Hud.Accent.r, S3Hud.Accent.g, S3Hud.Accent.b, 0.92f));
            else S3Hud.Panel(r);
            Color ink = it.held ? new Color(0.12f, 0.09f, 0.03f) : it.count > 0 ? S3Hud.Ink : S3Hud.Dim;
            float tx = x + pad + S3Hud.Key(x + pad, cy, it.key) + 10;
            float nw = S3Hud.Measure(it.name, S3Hud.Body).x;
            S3Hud.Text(new Rect(tx, cy - 12, nw + 2, 24), it.name, S3Hud.Body, ink, false);
            S3Hud.Text(new Rect(tx + nw + 10, cy - 12, 60, 24), $"×{it.count}", S3Hud.BodyBold, ink, false);
            x += cw + gap;
        }
    }

    /// <summary>The level's hint, on a card at the top.</summary>
    void DrawLevelHint(float w)
    {
        float bw = Mathf.Min(760, w - 48), x = (w - bw) / 2, y = 24, pad = 22;
        float th = S3Hud.Wrap.CalcHeight(new GUIContent(puzzle.Level.hint), bw - 2 * pad);
        var r = new Rect(x, y, bw, th + 82);
        hintBottom = r.yMax;
        S3Hud.Panel(r);
        S3Hud.Text(new Rect(x + pad, y + 16, bw, 16), "PUZZLE", S3Hud.Eyebrow, S3Hud.Accent, false);
        float kw = S3Hud.Measure("hide", S3Hud.Small).x;
        S3Hud.Text(new Rect(x + bw - pad - kw, y + 14, kw + 2, 20), "hide", S3Hud.Small, S3Hud.Dim, false);
        S3Hud.Key(x + bw - pad - kw - 32, y + 24, "H");
        S3Hud.Text(new Rect(x + pad, y + 36, bw, 26), puzzle.Level.title, S3Hud.BodyBold, S3Hud.Ink, false);
        S3Hud.Text(new Rect(x + pad, y + 64, bw - 2 * pad, th), puzzle.Level.hint, S3Hud.Wrap, new Color(1, 1, 1, 0.85f), false);
    }

    static readonly (string group, string keys)[] ControlLines =
    {
        ("Golf", "A/D or mouse aim  ·  W/S height (Shift fine)  ·  Space power, Space shoot  ·  Right-click cancel  ·  C level the view"),
        ("Ball", "Tab walk around  ·  E / F back to the ball  ·  B replay last spot (+1)  ·  R restart  ·  N start over"),
        ("Laser", "Click place / pick up  ·  Q/E turn  ·  1 / 2 or wheel switch piece  ·  H puzzle hint"),
        ("Walking", "WASD, Shift run, Space jump  ·  M next map  ·  Esc free the mouse"),
        ("Antipodes", "Straight up at full power, a ball comes down 37.7 m away"),
    };

    /// <summary>Every hole and puzzle, one column per map (the one you're on highlighted), and the controls.</summary>
    void DrawLevelSelect(float w, float h)
    {
        GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.6f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        float gap = 20, colW = Mathf.Min(360, (w - 112 - 2 * gap) / 3), rowH = 42, rowStep = rowH + 8, lineH = 28;
        int rows = 0;
        foreach (var l in levelLabels) rows = Mathf.Max(rows, l.Length);
        float cardW = 3 * colW + 2 * gap + 64;
        float levelsH = 96 + 32 + rows * rowStep;
        float cardH = levelsH + 30 + 32 + ControlLines.Length * lineH + 64;
        float x0 = (w - cardW) / 2, y0 = Mathf.Max(16, (h - cardH) / 2);
        S3Hud.Panel(new Rect(x0, y0, cardW, cardH), 1.45f);
        S3Hud.Text(new Rect(x0, y0 + 24, cardW, 56), "Select a level", S3Hud.Title, S3Hud.Ink, false);
        int current = world.HasGolf ? holeIndex : puzzleIndex;
        for (int m = 0; m < Maps.Length; m++)
        {
            float x = x0 + 32 + m * (colW + gap), y = y0 + 96;
            S3Hud.Text(new Rect(x + 2, y, colW, 18), MapTitle(Maps[m]).ToUpperInvariant(), S3Hud.Eyebrow, S3Hud.Accent, false);
            y += 32;
            for (int i = 0; i < levelLabels[m].Length; i++, y += rowStep)
                if (S3Hud.Button(new Rect(x, y, colW, rowH), levelLabels[m][i], Maps[m] == world.biome && i == current))
                {
                    JumpTo(Maps[m], i);
                    return;
                }
        }
        float cy = y0 + levelsH + 30;
        S3Hud.Text(new Rect(x0 + 34, cy, cardW, 18), "CONTROLS", S3Hud.Eyebrow, S3Hud.Accent, false);
        cy += 32;
        foreach (var (group, keys) in ControlLines)
        {
            S3Hud.Text(new Rect(x0 + 34, cy, 120, lineH), group, S3Hud.BodyBold, S3Hud.Ink, false);
            S3Hud.Text(new Rect(x0 + 154, cy, cardW - 180, lineH), keys, S3Hud.Small, S3Hud.Dim, false);
            cy += lineH;
        }
        S3Hud.Centered(w / 2, y0 + cardH - 30, "Click a level to play it   ·   L or Esc to close", S3Hud.Small, S3Hud.Dim);
    }

    void DrawMarker(Vector4 target, Color color, string label, Texture2D icon, float scale = 1)
    {
        if (!cam) cam = player.GetComponent<Camera>();
        Matrix4x4 frame = player.ViewFrame();
        Vector4 q = Matrix4x4.Scale(new Vector3(1, 1, -1)) * frame.transpose * target;
        var dir = new Vector3(q.x, q.y, -q.z);          // Unity camera space: +z forward
        // markers stay below the cards along the top (the hint card's bottom is last frame's)
        float k = S3Hud.Scale, w = S3Hud.Width, h = S3Hud.Height, m = 40, top = Mathf.Max(locked ? 196 : 152, HintVisible ? hintBottom + 14 : 0);
        Vector2 c = new Vector2(w / 2, h / 2), p;
        bool onScreen = false;
        if (dir.z > 1e-4f)
        {
            Vector3 sp = cam.WorldToScreenPoint(cam.transform.TransformPoint(dir.normalized * 10f));
            p = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
            onScreen = p.x > m && p.x < w - m && p.y > m && p.y < h - m;
        }
        else p = c + new Vector2(dir.x, -dir.y).normalized * 1e4f;   // behind: push towards its side
        if (!onScreen)
        {
            Vector2 v = p - c;
            if (v.sqrMagnitude < 1e-6f) v = Vector2.up;
            float s = Mathf.Min((w / 2 - m) / Mathf.Max(Mathf.Abs(v.x), 1e-4f), (h / 2 - m) / Mathf.Max(Mathf.Abs(v.y), 1e-4f));
            p = c + v * s;
            p.y = Mathf.Min(p.y, h - 120);
        }
        p.y = Mathf.Max(p.y, top);
        float size = 24 * scale;
        S3Hud.Icon(new Rect(p.x - size / 2, p.y - size / 2, size, size), icon, color);
        S3Hud.Tag(p.x, p.y + size / 2 + 6, label, new Color(1, 1, 1, color.a));
    }
}
