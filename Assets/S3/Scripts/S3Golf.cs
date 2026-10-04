using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Golf on the ground great-sphere of S^3: walk up to the ball, aim left/right by turning,
/// set the launch height with the mouse, hold the left button to charge and release to swing.
///
/// Straight up from any spot, a quarter lap (pi/2 * R) away, is the pole of the hemisphere:
/// every direction there leads back down. A drive hard enough to cross the pole lands on the
/// far side of the world, at the antipode for a vertical shot. Putts roll along great circles,
/// so a hard putt goes all the way round and comes back from behind you.
/// </summary>
[DefaultExecutionOrder(10)]
public class S3Golf : MonoBehaviour
{
    [Serializable]
    public class Club
    {
        public string name;
        public float maxSpeed;   // m/s at full power
        public bool aimable;     // launch height from the mouse; otherwise flat (putter)
        public Club(string name, float maxSpeed, bool aimable) { this.name = name; this.maxSpeed = maxSpeed; this.aimable = aimable; }
    }

    public S3Player player;
    public float ballRadius = 0.1f;
    public float fairwayFriction = 1.7f;   // rolling deceleration, m/s^2
    public float greenFriction = 0.85f;
    public float sinkSpeed = 3.5f;         // faster than this and the ball skips over the cup
    public float reach = 2.6f;             // how close you must be to swing
    [Tooltip("With R = 12, 19.9 m/s straight up just crosses the pole; 21.5 puts that at ~94% power.")]
    public Club[] clubs =
    {
        new Club("Driver", 21.5f, true),
        new Club("Iron", 14f, true),
        new Club("Putter", 8f, false),
    };

    [Header("Aim")]
    public float minAngle = 2f;
    public float maxAngle = 90f;
    public float startAngle = 20f;
    public float aimDegreesPerPixel = 0.12f;

    [Header("Flight camera")]
    public float leash = 4.5f;               // the camera trails the ball at most this far
    public float followMinHeight = 1.2f;
    public float followTriggerHeight = 2.5f; // only shots that climb this high take the camera

    enum State { Ready, Moving, Sunk, Finished }

    const float Step = 1f / 240f;

    S3World world;
    S3Ball ball;
    State state;
    Renderer ballRenderer, flagRenderer, arrowRenderer, guideRenderer;
    int holeIndex, strokes, total, club;
    bool charging, aiming, forceAim;
    float charge, power, accumulator, moveTime, sunkTimer, aimAngle;
    float shotMaxHeight, groundedTime;
    Vector4 lastShot;
    // flight camera
    float followWeight;
    Vector4 camPos, camUp, camFwd;
    string message = "";
    float messageUntil;
    GUIStyle big, small, center;

    S3World.Hole Hole => world.holes[holeIndex];
    float R => world.radius;

    void Start() => Init();

    /// <summary>Creates the ball, flag and aim helpers and starts hole 1 (also used by the capture tool).</summary>
    public void Init()
    {
        world = S3World.Instance;
        if (!player) player = FindAnyObjectByType<S3Player>();
        player.cameraFilter = FlightCamera;
        ball = new S3Ball { R = R, radius = ballRadius };
        ballRenderer = world.CreateRenderer("Golf Ball", S3Scenery.BallMesh(ballRadius), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        flagRenderer = world.CreateRenderer("Flag", S3Scenery.FlagMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        arrowRenderer = world.CreateRenderer("Aim Arrow", S3Scenery.ArrowMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        guideRenderer = world.CreateRenderer("Launch Guide", S3Scenery.GuideMesh(), world.objectMaterial, Matrix4x4.identity).GetComponent<Renderer>();
        total = 0;
        StartHole(0);
    }

    void StartHole(int index)
    {
        holeIndex = index;
        strokes = 0;
        club = 0;
        aimAngle = startAngle;
        followWeight = 0;
        ball.Place(Hole.tee);
        lastShot = ball.Foot;
        state = State.Ready;
        world.SetActiveCup(Hole.cup);
        Vector4 toTee = S3.DirTo(Hole.cup, Hole.tee);
        S3World.SetFrame(flagRenderer, S3.FrameAt(Hole.cup, S3.EY, toTee == Vector4.zero ? S3.EZ : toTee));
        StandAtBall();
        float dist = world.GroundDistance(Hole.tee, Hole.cup);
        Say($"Hole {index + 1}  ·  Par {Hole.par}  ·  {dist:0} m" +
            (dist > 0.97f * Mathf.PI * R ? "\nThe cup is at your antipode. Every direction leads there,\nincluding straight up." : ""), 5f);
        UpdateVisuals();
    }

    /// <summary>
    /// Puts the player behind and a little left of the ball, facing along the line to the cup.
    /// The side offset keeps the ball on screen and lets you read the launch height from the guide.
    /// </summary>
    void StandAtBall()
    {
        Vector4 foot = ball.Foot;
        Vector4 toCup = S3.DirTo(foot, Hole.cup);
        if (toCup == Vector4.zero) toCup = S3.Ortho(player.Forward, foot, S3.EY);
        if (toCup == Vector4.zero) toCup = S3.Ortho(S3.EZ, foot, S3.EY);
        Vector4 right = S3.FrameAt(foot, S3.EY, toCup).GetColumn(0);
        Vector4 offset = -1.5f * toCup - 0.75f * right;
        Vector4 stand = S3.Walk(foot, offset.normalized, offset.magnitude / R);
        player.Teleport(stand, S3.Ortho(toCup, stand, S3.EY), AimPitch());
    }

    void Update()
    {
        if (ball == null || world == null) return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float dt = Time.deltaTime;

        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) club = 0;
            if (kb.digit2Key.wasPressedThisFrame) club = 1;
            if (kb.digit3Key.wasPressedThisFrame) club = 2;
            if (kb.qKey.wasPressedThisFrame) club = (club + clubs.Length - 1) % clubs.Length;
            if (kb.eKey.wasPressedThisFrame) club = (club + 1) % clubs.Length;
            if (kb.rKey.wasPressedThisFrame && state != State.Sunk) StandAtBall();
            if (kb.bKey.wasPressedThisFrame && (state == State.Ready || state == State.Moving) && strokes > 0)
            {
                ball.Place(lastShot);
                state = State.Ready;
                followWeight = 0;
                strokes++; total++;
                Say("Ball returned to its last spot (+1 stroke)", 2.5f);
            }
            if (kb.nKey.wasPressedThisFrame) { total = 0; StartHole(0); }
        }

        UpdateAim(mouse != null && Cursor.lockState == CursorLockMode.Locked ? mouse.delta.ReadValue().y : 0, dt);
        switch (state)
        {
            case State.Ready: HandleSwing(mouse); break;
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
    }

    bool NearBall => world.GroundDistance(player.Foot, ball.Foot) < reach;

    Vector4 ShotDirection => S3.Ortho(player.Forward, ball.pos, S3.UpAt(ball.pos));

    /// <summary>Camera pitch for the current launch height: tilts up a little, keeping the ball in view.</summary>
    float AimPitch() => Mathf.Lerp(-0.45f, -0.32f, Mathf.InverseLerp(minAngle, maxAngle, aimAngle));

    // ------------------------------------------------------------------ aiming and swinging

    void UpdateAim(float mouseDy, float dt)
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked || forceAim;
        aiming = state == State.Ready && locked && NearBall && clubs[club].aimable;
        player.externalPitch = aiming;
        if (!aiming) return;
        aimAngle = Mathf.Clamp(aimAngle + mouseDy * aimDegreesPerPixel, minAngle, maxAngle);
        player.pitch = Mathf.MoveTowards(player.pitch, AimPitch(), dt * 2f);
    }

    void HandleSwing(Mouse mouse)
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        if (mouse == null || !locked || !NearBall) { charging = false; return; }
        if (mouse.leftButton.wasPressedThisFrame && player.LockedOnFrame != Time.frameCount)
        {
            charging = true;
            charge = 0;
        }
        if (!charging) return;
        if (mouse.rightButton.wasPressedThisFrame) { charging = false; return; }   // cancel
        charge += Time.deltaTime;
        power = Mathf.PingPong(charge * 0.75f, 1f);
        if (mouse.leftButton.wasReleasedThisFrame)
        {
            charging = false;
            Shoot(power);
        }
    }

    float LaunchAngle => clubs[club].aimable ? aimAngle : 0f;

    /// <summary>
    /// Meter position that just carries a vertical shot over the pole: energy for the climb of
    /// pi/2 * R, plus ~3.6% for air drag (measured with the integrator).
    /// </summary>
    float PoleCrossingPower()
    {
        float v = Mathf.Sqrt(2 * ball.gravity * (0.5f * Mathf.PI * R - ballRadius)) * 1.036f;
        return Mathf.InverseLerp(0.04f, 1f, v / clubs[club].maxSpeed) + (v > clubs[club].maxSpeed ? 1 : 0);
    }

    float ShotSpeed(float p) => Mathf.Lerp(0.04f, 1f, p) * clubs[club].maxSpeed;

    void Shoot(float p)
    {
        lastShot = ball.Foot;
        ball.Launch(ShotDirection, ShotSpeed(p), LaunchAngle * Mathf.Deg2Rad);
        strokes++; total++;
        state = State.Moving;
        moveTime = accumulator = 0;
        shotMaxHeight = groundedTime = 0;
    }

    bool OnGreen(Vector4 foot) => S3.Dist(foot, Hole.cup) * R < world.greenRadius;

    void Simulate(float dt)
    {
        accumulator += Mathf.Min(dt, 0.1f);
        moveTime += dt;
        while (accumulator >= Step)
        {
            accumulator -= Step;
            ball.Step(Step, OnGreen(ball.Foot) ? greenFriction : fairwayFriction, world.obstacles);
            shotMaxHeight = Mathf.Max(shotMaxHeight, ball.HeightM - ballRadius);
            groundedTime = ball.grounded ? groundedTime + Step : 0;

            float toCup = S3.Dist(ball.Foot, Hole.cup) * R;
            if (toCup < world.cupRadius && ball.HeightM < ballRadius * 1.6f && ball.Speed < sinkSpeed)
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
                if (OnGreen(ball.Foot)) club = 2;
                else if (club == 0 && world.GroundDistance(ball.Foot, Hole.cup) < 14f) club = 1;
                if (shotMaxHeight > 0.5f * Mathf.PI * R - 1f && world.GroundDistance(lastShot, ball.Foot) > 0.5f * Mathf.PI * R)
                    Say("Over the pole!\nIt came down on the far side of the world.", 3.5f);
                return;
            }
        }
        if (moveTime > 40f) { ball.vel = Vector4.zero; state = State.Ready; }
    }

    // ------------------------------------------------------------------ flight camera

    void UpdateFollow(float dt)
    {
        bool want = state == State.Moving && shotMaxHeight > followTriggerHeight && groundedTime < 0.35f;
        if (want && followWeight <= 0)
        {
            Matrix4x4 c = player.CameraFrame;
            camPos = c.GetColumn(3);
            camUp = c.GetColumn(1);
            camFwd = c.GetColumn(2);
        }
        followWeight = Mathf.MoveTowards(followWeight, want ? 1 : 0, dt / (want ? 0.5f : 0.7f));
        if (followWeight > 0) TrackBall(dt);
    }

    /// <summary>
    /// Trail the ball on a leash, look at it, and keep "up" pointing away from the nearest ground.
    /// When the camera crosses the pole, up flips and the view rolls over smoothly.
    /// </summary>
    void TrackBall(float dt)
    {
        float d = S3.Dist(camPos, ball.pos) * R;
        if (d > leash) camPos = S3.Walk(camPos, S3.DirTo(camPos, ball.pos), (d - leash) / R);
        if (Mathf.Asin(Mathf.Clamp(camPos.y, -1f, 1f)) * R < followMinHeight)
            camPos = S3.Lift(S3.Foot(camPos), followMinHeight / R);

        // stay out of trees and houses
        foreach (var o in world.obstacles)
        {
            if (o.height < 2f) continue;
            float clearance = o.radius + 1.6f;
            Vector4 foot = S3.Foot(camPos);
            float hd = S3.Dist(foot, o.foot) * R;
            if (hd >= clearance || hd < 1e-4f || Mathf.Asin(Mathf.Clamp(camPos.y, -1f, 1f)) * R > o.height + 1.5f) continue;
            float h = Mathf.Asin(Mathf.Clamp(camPos.y, -1f, 1f));
            camPos = S3.Lift(S3.Walk(foot, -S3.DirTo(foot, o.foot), (clearance - hd) / R), h);
        }

        Vector4 f = S3.DirTo(camPos, ball.pos);
        camFwd = f != Vector4.zero ? f : S3.Ortho(camFwd, camPos);

        Vector4 prevUp = S3.Ortho(camUp, camPos, camFwd);
        if (prevUp == Vector4.zero) prevUp = S3.FrameAt(camPos, camUp, camFwd).GetColumn(1);
        Vector4 away = S3.EY - camPos.y * camPos;
        Vector4 target = away.magnitude > 1e-4f ? S3.Ortho(away, camPos, camFwd) : Vector4.zero;
        if (target == Vector4.zero) target = prevUp;

        // rotate prevUp towards target about the view axis (handles a full 180° roll)
        float angle = Mathf.Acos(Mathf.Clamp(Vector4.Dot(prevUp, target), -1f, 1f));
        Vector4 side = S3.Ortho(target, camPos, camFwd, prevUp);
        if (side == Vector4.zero) side = S3.FrameAt(camPos, prevUp, camFwd).GetColumn(0);
        float turn = angle * (1 - Mathf.Exp(-5f * dt));
        camUp = Mathf.Cos(turn) * prevUp + Mathf.Sin(turn) * side;
    }

    Matrix4x4 FlightCamera(Matrix4x4 playerCamera)
    {
        if (followWeight <= 0) return playerCamera;
        Matrix4x4 follow = S3.FrameAt(camPos, camUp, camFwd);
        float s = Mathf.SmoothStep(0, 1, followWeight);
        if (s >= 1) return follow;
        Vector4 a = playerCamera.GetColumn(3);
        Vector4 pos = S3.Walk(a, S3.DirTo(a, camPos), S3.Dist(a, camPos) * s);
        Vector4 up = Vector4.Lerp(playerCamera.GetColumn(1), camUp, s);
        Vector4 fwd = Vector4.Lerp(playerCamera.GetColumn(2), camFwd, s);
        return S3.FrameAt(pos, up, fwd);
    }

    // ------------------------------------------------------------------ visuals

    void UpdateVisuals()
    {
        ballRenderer.enabled = state != State.Sunk && state != State.Finished;
        S3World.SetFrame(ballRenderer, ball.frame);
        bool ready = state == State.Ready && NearBall;
        Vector4 d = ready ? ShotDirection : Vector4.zero;
        arrowRenderer.enabled = ready && d != Vector4.zero;
        guideRenderer.enabled = aiming && d != Vector4.zero;
        if (d == Vector4.zero) return;
        S3World.SetFrame(arrowRenderer, S3.FrameAt(ball.Foot, S3.EY, d));
        if (aiming)
            S3World.SetFrame(guideRenderer, S3.FrameAt(ball.pos, S3.UpAt(ball.pos), d) *
                                            S3.PlaneRotation(S3.EZ, S3.EY, aimAngle * Mathf.Deg2Rad));
    }

    void Say(string text, float seconds)
    {
        message = text;
        messageUntil = Time.realtimeSinceStartup + seconds;
    }

    // ------------------------------------------------------------------ capture / test hooks

    /// <summary>Show the aim view at a given launch height without input.</summary>
    public void DebugAim(float angleDeg)
    {
        forceAim = true;
        club = 0;
        StandAtBall();
        aimAngle = angleDeg;
        player.pitch = AimPitch();
        UpdateAim(0, 0);
        UpdateVisuals();
        forceAim = false;
    }

    /// <summary>Restart hole 1 and fly a driver shot for the given time with fixed steps.</summary>
    public void DebugFly(float shotPower, float angleDeg, float seconds)
    {
        StartHole(0);
        club = 0;
        aimAngle = angleDeg;
        Shoot(shotPower);
        for (float t = 0; t < seconds; t += 1 / 60f)
        {
            if (state == State.Moving) Simulate(1 / 60f);
            UpdateFollow(1 / 60f);
        }
        UpdateVisuals();
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
        }
        float w = Screen.width, h = Screen.height;

        Shadowed(new Rect(18, 12, 900, 30), $"Hole {holeIndex + 1}/{world.holes.Count}   Par {Hole.par}   Strokes {strokes}   Total {total}", big);
        float pin = world.GroundDistance(ball.Foot, Hole.cup);
        Shadowed(new Rect(18, 42, 900, 26), $"Club: {clubs[club].name}  (1/2/3 or Q/E)      Pin: {pin:0.0} m      Pole: {0.5f * Mathf.PI * R:0.0} m straight up", small);

        // crosshair
        Shadowed(new Rect(w / 2 - 10, h / 2 - 14, 20, 28), "+", center);

        string hint;
        if (Cursor.lockState != CursorLockMode.Locked) hint = "Click to capture the mouse";
        else if (state == State.Ready && NearBall)
            hint = charging ? "Release to swing  (right-click cancels)"
                 : clubs[club].aimable ? "Turn to aim · mouse up/down sets height · hold left mouse to charge"
                 : "Putter rolls along the ground · hold left mouse to charge";
        else if (state == State.Ready) hint = $"Walk to the ball ({world.GroundDistance(player.Foot, ball.Foot):0} m away), or press R to jump to it";
        else if (state == State.Moving) hint = $"Ball height {Mathf.Max(0, ball.HeightM - ballRadius):0.0} m";
        else hint = "";
        Shadowed(new Rect(0, h - 120, w, 30), hint, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
        Shadowed(new Rect(18, h - 34, w, 26), "WASD walk · Shift run · Space jump · Mouse look · R stand at ball · B replay last spot · N restart · Esc free mouse", small);

        if (aiming)
            Shadowed(new Rect(0, h - 200, w, 34), $"HEIGHT {aimAngle:0}°", new GUIStyle(big) { alignment = TextAnchor.MiddleCenter, fontSize = 26 });

        if (charging)
        {
            float bw = 320, bh = 18, x = w / 2 - bw / 2, y = h - 160;
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(x - 3, y - 3, bw + 6, bh + 6), Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(1f, 0.25f, 0.15f), power);
            GUI.DrawTexture(new Rect(x, y, bw * power, bh), Texture2D.whiteTexture);
            float poleMark = PoleCrossingPower();
            if (aiming && aimAngle > 75 && poleMark <= 1)
            {
                GUI.color = new Color(1, 1, 1, 0.85f);
                GUI.DrawTexture(new Rect(x + bw * poleMark, y - 5, 2, bh + 10), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            Shadowed(new Rect(x, y - 26, bw, 24), $"{clubs[club].name}  {ShotSpeed(power):0.0} m/s", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        }

        if (Time.realtimeSinceStartup < messageUntil && message.Length > 0)
            Shadowed(new Rect(0, h * 0.22f, w, 140), message, center);
    }

    static void Shadowed(Rect r, string text, GUIStyle style)
    {
        var c = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
        GUI.color = c;
        GUI.Label(r, text, style);
    }
}
