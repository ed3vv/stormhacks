using System;
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
///   * One club: low launches roll like putts, high ones fly. Full power straight up crosses the
///     pole of the hemisphere (pi/2 * R overhead) and lands on the far side of the world.
///   * After every shot you are on foot where you hit from: walk to the ball (it has its own
///     waypoint) and press E or Tab next to it to address it again. Tab at the ball steps
///     away from it to look around.
///   * M switches between the Meadow and Desert maps.
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
    public float rollFrictionUnits = 0.54f;   // R/s^2 rolling deceleration
    public float bounceSpeedUnits = 0.14f;    // below this landing speed the ball rolls instead of bouncing
    public float restitution = 0.38f;
    public float bounceGrip = 0.9f;
    public float airDrag = 0.035f;            // per second

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

    [Header("Flight camera")]
    public float leash = 4.1f;                // trails the ball at most this far (ed3vv: 0.34 R)
    public float followMinHeight = 1.0f;
    public float followTriggerHeight = 1.0f;  // shots that climb this high take the camera
    public float lookRate = 5f;
    public float settleTime = 0.7f;

    enum State { Ready, Moving, Sunk, Finished }

    const float Step = 1f / 240f;

    S3World world;
    S3Ball ball;
    State state;
    Renderer ballRenderer, flagRenderer, arrowRenderer, guideRenderer;
    int holeIndex, strokes, total;
    bool powerSelecting, exploring;
    float power, powerPhase, accumulator, moveTime, sunkTimer, aimAngle;
    float shotMaxHeight, groundedTime;
    Vector4 lastShot;
    float lookPitch;   // free vertical look while addressing the ball (radians, relative to the shot)
    Vector4 aimDir;      // shot direction: horizontal unit tangent at the ball's foot
    Vector4 viewDir;     // the ball camera's heading, eased towards aimDir while the ball moves
    bool wasGrounded;
    // flight camera
    float followWeight;
    Vector4 camPos, camUp, camFwd;
    string message = "";
    float messageUntil;
    GUIStyle big, small, center, hintStyle, meterStyle, markerStyle, iconStyle;
    Camera cam;

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
        ball = NewBall(R);
        ball.radius = ballRadius;
        ConfigureBall(ball);
        ballRenderer = world.CreateRenderer("Golf Ball", S3Scenery.BallMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        flagRenderer = world.CreateRenderer("Flag", S3Scenery.FlagMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        arrowRenderer = world.CreateRenderer("Aim Arrow", S3Scenery.ArrowMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        guideRenderer = world.CreateRenderer("Launch Guide", S3Scenery.GuideMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        total = 0;
        StartHole(0);
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
        Vector4 toTee = S3.DirTo(Hole.cup, Hole.tee);
        S3World.SetFrame(flagRenderer, S3.FrameAt(Hole.cup, S3.EY, toTee == Vector4.zero ? S3.EZ : toTee));
        aimDir = viewDir = DirectionToCup();
        lookPitch = 0;
        exploring = false;
        player.inputEnabled = false;
        float dist = world.GroundDistance(Hole.tee, Hole.cup);
        Say($"Hole {index + 1}  ·  Par {Hole.par}  ·  {dist:0} m" +
            (dist > 0.97f * Mathf.PI * R ? "\nThe cup is at your antipode. Every direction leads there,\nincluding straight up." : ""), 5f);
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
        if (ball == null || world == null) return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float dt = Time.deltaTime;
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        Vector2 md = mouse != null && locked ? mouse.delta.ReadValue() : Vector2.zero;

        if (kb != null)
        {
            if (kb.tabKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
            {
                if (!exploring) { if (kb.tabKey.wasPressedThisFrame) SetExploring(true); }
                else if (state == State.Ready && NearBall)
                {
                    // aim where you were facing as you walked up
                    Vector4 facing = S3.Ortho(player.Forward, ball.Foot, S3.EY);
                    if (facing != Vector4.zero) aimDir = viewDir = facing;
                    lookPitch = 0;
                    SetExploring(false);
                }
                else if (state == State.Ready) Say($"Walk to your ball first ({world.GroundDistance(player.Foot, ball.Foot):0} m)", 2f);
            }
            if (kb.mKey.wasPressedThisFrame)
            {
                var next = world.biome == S3World.Biome.Meadow ? S3World.Biome.Desert : S3World.Biome.Meadow;
                world.SetBiome(next);
                Init();
                Say(next == S3World.Biome.Desert ? "Desert Oasis" : "Meadow", 2.5f);
                return;
            }
            if (kb.rKey.wasPressedThisFrame && state != State.Sunk) { total -= strokes; StartHole(holeIndex); }
            if (kb.nKey.wasPressedThisFrame) { total = 0; StartHole(0); }
            if (kb.bKey.wasPressedThisFrame && (state == State.Ready || state == State.Moving) && strokes > 0)
            {
                ball.Place(lastShot);
                state = State.Ready;
                followWeight = 0;
                powerSelecting = false;
                strokes++; total++;
                exploring = false;
                player.inputEnabled = false;
                Say("Ball returned to its last spot (+1 stroke)", 2.5f);
            }
            if (kb.spaceKey.wasPressedThisFrame && state == State.Ready && !exploring)
            {
                if (!powerSelecting) { powerSelecting = true; powerPhase = 0; power = 0; }
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

        if (state == State.Ready && !exploring)
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
                        Say($"Course complete!  {total} strokes (par {par}).\nPress N to play again.", 1e6f);
                    }
                }
                break;
        }
        UpdateFollow(dt);
        UpdateVisuals();
        UpdateSeeThrough(dt);
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
        strokes++; total++;
        state = State.Moving;
        moveTime = accumulator = 0;
        shotMaxHeight = groundedTime = 0;
        wasGrounded = false;
        SetExploring(true);   // watch it go, then walk to it
    }

    void Simulate(float dt)
    {
        accumulator += Mathf.Min(dt, 0.1f);
        moveTime += dt;
        float friction = rollFrictionUnits * R;
        while (accumulator >= Step)
        {
            accumulator -= Step;
            ball.Step(Step, friction, world.obstacles);
            shotMaxHeight = Mathf.Max(shotMaxHeight, ball.HeightM - ballRadius);
            groundedTime = ball.grounded ? groundedTime + Step : 0;

            // like ed3vv: after landing, the camera turns to follow the way the ball is travelling
            if (ball.grounded && !wasGrounded)
            {
                Vector4 travel = S3.Ortho(ball.vel, ball.Foot, S3.EY);
                if (travel != Vector4.zero && ball.Speed > 0.3f) aimDir = travel;
            }
            wasGrounded = ball.grounded;

            // the cup takes the ball whenever it rolls or lands in it (ed3vv has no lip-out)
            if (S3.Dist(ball.Foot, Hole.cup) * R < world.cupRadius && ball.HeightM < ballRadius * 1.3f)
            {
                state = State.Sunk;
                sunkTimer = 3f;
                int diff = strokes - Hole.par;
                string name = strokes == 1 ? "HOLE IN ONE!" :
                    diff <= -2 ? "Eagle!" : diff == -1 ? "Birdie!" : diff == 0 ? "Par" : diff == 1 ? "Bogey" : $"+{diff}";
                Say($"{name}\nSunk in {strokes}", 3f);
                return;
            }
            if (ball.grounded && ball.Speed < 0.05f)
            {
                ball.vel = Vector4.zero;
                state = State.Ready;
                if (shotMaxHeight > 0.5f * Mathf.PI * R - 1f && world.GroundDistance(lastShot, ball.Foot) > 0.5f * Mathf.PI * R)
                    Say("Over the pole!\nIt came down on the far side of the world.", 3.5f);
                return;
            }
        }
        if (moveTime > 40f) { ball.vel = Vector4.zero; state = State.Ready; }
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
        if (followWeight <= 0) return baseFrame;
        Matrix4x4 follow = S3.FrameAt(camPos, camUp, camFwd);
        float s = Mathf.SmoothStep(0, 1, followWeight);
        if (s >= 1) return follow;
        Vector4 a = baseFrame.GetColumn(3);
        Vector4 pos = S3.Walk(a, S3.DirTo(a, camPos), S3.Dist(a, camPos) * s);
        Vector4 up = Vector4.Lerp(baseFrame.GetColumn(1), camUp, s);
        Vector4 fwd = Vector4.Lerp(baseFrame.GetColumn(2), camFwd, s);
        return S3.FrameAt(pos, up, fwd);
    }

    void UpdateFollow(float dt)
    {
        bool want = state == State.Moving && shotMaxHeight > followTriggerHeight && groundedTime < 0.35f;
        if (want && followWeight <= 0)
        {
            Matrix4x4 c = player.ViewFrame();
            camPos = c.GetColumn(3);
            camUp = c.GetColumn(1);
            camFwd = c.GetColumn(2);
        }
        followWeight = Mathf.MoveTowards(followWeight, want ? 1 : 0, dt / (want ? 0.4f : settleTime));
        if (followWeight > 0) TrackBall(dt);
    }

    /// <summary>
    /// Trail the ball on a leash and turn smoothly to keep it in view (ed3vv's flight camera).
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
        Vector4 prevFwd = S3.Ortho(camFwd, camPos);
        Vector4 f = look == Vector4.zero ? prevFwd : (prevFwd * (1 - k) + look * k);
        f = S3.Ortho(f, camPos);
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

    // ------------------------------------------------------------------ visuals

    void UpdateVisuals()
    {
        ballRenderer.enabled = state != State.Sunk && state != State.Finished;
        S3World.SetFrame(ballRenderer, ball.frame);
        bool aiming = state == State.Ready && !exploring;
        Vector4 d = ShotDirection;
        arrowRenderer.enabled = guideRenderer.enabled = aiming && d != Vector4.zero;
        if (!arrowRenderer.enabled) return;
        S3World.SetFrame(arrowRenderer, S3.FrameAt(ball.Foot, S3.EY, d));
        S3World.SetFrame(guideRenderer, S3.FrameAt(ball.pos, S3.UpAt(ball.pos), d) *
                                        S3.PlaneRotation(S3.EZ, S3.EY, aimAngle * Mathf.Deg2Rad));
    }

    void Say(string text, float seconds)
    {
        message = text;
        messageUntil = Time.realtimeSinceStartup + seconds;
    }

    /// <summary>Meter position whose shot just carries straight up over the pole.</summary>
    float PoleCrossingPower()
    {
        float need = Mathf.Sqrt(2 * ball.gravity * (0.5f * Mathf.PI * R - ballRadius)) * 1.02f;
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

    public string DebugState =>
        $"state {state}, ball height {ball.HeightM:0.0} m, ground distance from tee {world.GroundDistance(ball.Foot, Hole.tee):0.0} m, follow {followWeight:0.00}";

    // ------------------------------------------------------------------ HUD

    void OnGUI()
    {
        if (world == null || ball == null) return;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            center = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hintStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            meterStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            markerStyle = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
            iconStyle = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 28 };
        }
        float w = Screen.width, h = Screen.height;

        DrawWaypoint();

        Shadowed(new Rect(18, 12, 900, 30), $"{(world.biome == S3World.Biome.Desert ? "Desert Oasis" : "Meadow")}   Hole {holeIndex + 1}/{world.holes.Count}   Par {Hole.par}   Strokes {strokes}   Total {total}", big);
        float pin = world.GroundDistance(ball.Foot, Hole.cup);
        Shadowed(new Rect(18, 42, 900, 26), $"Pin: {pin:0.0} m      HEIGHT {aimAngle:0}°      Pole: {0.5f * Mathf.PI * R:0.0} m straight up", small);

        string hint;
        float toBall = world.GroundDistance(player.Foot, ball.Foot);
        if (Cursor.lockState != CursorLockMode.Locked) hint = "Click to capture the mouse";
        else if (state == State.Moving) hint = ball.grounded ? "Ball rolling…" : $"Ball airborne… {Mathf.Max(0, ball.HeightM - ballRadius):0.0} m up";
        else if (state == State.Ready && exploring)
            hint = NearBall ? "Press E to address the ball" : $"Walk to your ball · {toBall:0} m · WASD, Shift to run";
        else if (state == State.Ready)
            hint = powerSelecting ? "Power moving · Space to shoot · right-click to re-aim"
                                  : "A/D or mouse aims · W/S height (Shift = fine) · mouse up/down looks, C levels · Space for power";
        else hint = "";
        Shadowed(new Rect(0, h - 120, w, 30), hint, hintStyle);
        Shadowed(new Rect(18, h - 34, w, 26), "Space power/shoot · E address ball · Tab walk around · M switch map · R restart hole · B replay last spot (+1) · N new game · Esc free mouse", small);

        if (powerSelecting)
        {
            float bw = 320, bh = 18, x = w / 2 - bw / 2, y = h - 160;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(x - 3, y - 3, bw + 6, bh + 6), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.25f, 0.15f), power);
            GUI.DrawTexture(new Rect(x, y, bw * power, bh), Texture2D.whiteTexture);
            float poleMark = PoleCrossingPower();
            if (aimAngle > 75 && poleMark <= 1)
            {
                GUI.color = new Color(1, 1, 1, 0.85f);
                GUI.DrawTexture(new Rect(x + bw * poleMark, y - 5, 2, bh + 10), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            Shadowed(new Rect(x, y - 26, bw, 24), $"POWER {power * 100:0}%   {LaunchSpeed(power, R):0.0} m/s", meterStyle);
        }

        if (Time.realtimeSinceStartup < messageUntil && message.Length > 0)
            Shadowed(new Rect(0, h * 0.22f, w, 140), message, center);
    }

    /// <summary>
    /// Waypoints: the flag (always) and your ball (whenever you're not addressing it), each with
    /// its distance from you. Targets are projected exactly like the shader projects geometry;
    /// off screen (or behind you) a marker sticks to the screen edge, pointing the way.
    /// </summary>
    void DrawWaypoint()
    {
        if (state == State.Finished) return;
        DrawMarker(S3.Lift(Hole.cup, 2.9f / R), new Color(1f, 0.85f, 0.25f), world.GroundDistance(player.ViewFrame().GetColumn(3), Hole.cup), "▼", "◆");
        if (exploring && state != State.Sunk)
            DrawMarker(S3.Lift(ball.Foot, (ballRadius * 2 + 0.5f) / R), new Color(0.55f, 0.95f, 1f), world.GroundDistance(player.Foot, ball.Foot), "●", "●");
    }

    void DrawMarker(Vector4 target, Color color, float distance, string onIcon, string edgeIcon)
    {
        if (!cam) cam = player.GetComponent<Camera>();
        Matrix4x4 frame = player.ViewFrame();
        Vector4 q = Matrix4x4.Scale(new Vector3(1, 1, -1)) * frame.transpose * target;
        var dir = new Vector3(q.x, q.y, -q.z);          // Unity camera space: +z forward
        float w = Screen.width, h = Screen.height, m = 24;
        Vector2 c = new Vector2(w / 2, h / 2), p;
        bool onScreen = false;
        if (dir.z > 1e-4f)
        {
            Vector3 sp = cam.WorldToScreenPoint(cam.transform.TransformPoint(dir.normalized * 10f));
            p = new Vector2(sp.x, h - sp.y);
            onScreen = p.x > m && p.x < w - m && p.y > m && p.y < h - m;
        }
        else p = c + new Vector2(dir.x, -dir.y).normalized * 1e4f;   // behind: push towards its side
        if (!onScreen)
        {
            Vector2 v = p - c;
            if (v.sqrMagnitude < 1e-6f) v = Vector2.up;
            float s = Mathf.Min((w / 2 - m) / Mathf.Max(Mathf.Abs(v.x), 1e-4f), (h / 2 - m) / Mathf.Max(Mathf.Abs(v.y), 1e-4f));
            p = c + v * s;
        }
        GUI.color = color;
        Shadowed(new Rect(p.x - 20, p.y - 22, 40, 34), onScreen ? onIcon : edgeIcon, iconStyle);
        GUI.color = Color.white;
        Shadowed(new Rect(p.x - 60, p.y + 10, 120, 22), $"{distance:0} m", markerStyle);
    }

    static void Shadowed(Rect r, string text, GUIStyle style)
    {
        var c = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.75f * c.a);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        GUI.color = c;
        GUI.Label(r, text, style);
    }
}
