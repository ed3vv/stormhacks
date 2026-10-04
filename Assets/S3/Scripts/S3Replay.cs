using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Instant replay, after main's (src/game/golf/game.js, "instant replay"): watch every shot of the hole again,
/// one after another, from outside the world, each tracing its path in its own colour (earlier paths stay up). In the replay's coordinates the whole of S^3 you can stand in is a solid unit ball
/// (see S3/Replay and <see cref="ToBall"/>): the ground is its surface and height above the ground is depth
/// towards the centre. An ordinary 3D camera sits off the ball, far enough to take all of it in, with the cup at
/// the bottom of the frame, and the mouse orbits it from there. The shell and the scenery are drawn as glass so
/// the flight inside stays visible: a drive over the pole is seen passing clean through the middle of the world.
///
/// It plays at 1.5x speed, pauses a beat between shots and on the last frame, and runs again, until you leave it.
/// Everything lives on its own layer, seen only by its own camera; the main camera is switched off meanwhile.
/// </summary>
public class S3Replay
{
    public const float Speed = 1.5f;          // one and a half times real time
    public const float Sample = 1f / 60f;     // seconds of flight between recorded samples
    const float Tail = 1f;                    // seconds held on the last frame before it runs again
    const int Pause = 18;                     // samples held where one shot stopped before the next is hit (0.2 s at Speed)
    const float TrailRadius = 0.009f, StartDot = 0.016f;   // ball coordinates: the path, and the dot where each shot was hit
    static readonly Color[] ShotColors =
    {
        new Color(1f, 0.86f, 0.25f), new Color(0.35f, 0.85f, 1f), new Color(1f, 0.45f, 0.72f),
        new Color(0.62f, 1f, 0.42f), new Color(1f, 0.58f, 0.25f), new Color(0.76f, 0.58f, 1f),
    };
    const float EyeDistance = 3.1f;           // the world has radius 1 in the replay's own coordinates
    const float EyeTilt = 22f;                // degrees the camera starts above level with the cup's horizon
    const float Look = 0.25f;                 // degrees the camera orbits per pixel of mouse
    const float MaxTilt = 85f;                // short of straight above or below, where its up would vanish
    const float ShellAlpha = 0.17f, SceneryAlpha = 0.5f;   // glass: the ground is thinner than the props
    const int Layer = 31;
    static readonly Color Space = new Color(0.05f, 0.07f, 0.12f);   // behind the world
    static readonly Vector3 Light = new Vector3(0.4f, 0.75f, 0.5f).normalized;
    static readonly float Bend = (Mathf.PI / 2 - 1) / (Mathf.PI / 2 * Mathf.PI / 2);

    public bool Active { get; private set; }
    /// <summary>How many shots it plays, and which one is playing now.</summary>
    public int Shots => starts.Count;
    public int CurrentShot
    {
        get
        {
            float at = Progress * (track.Count - 1);
            int k = 0;
            while (k + 1 < starts.Count && starts[k + 1] <= at) k++;
            return k;
        }
    }
    /// <summary>Seconds since it started (real time).</summary>
    public float Age { get; private set; }
    public float Progress => Mathf.Clamp01(elapsed / Mathf.Max(duration, 1e-6f));

    readonly Camera mainCamera;
    readonly Camera camera;
    readonly List<Object> owned = new List<Object>();
    readonly List<Vector4> track = new List<Vector4>();   // every shot back to back, with pauses between
    readonly List<int> starts = new List<int>();           // where each shot starts in track
    readonly List<int> ends = new List<int>();
    readonly Material trailMaterial;
    readonly Renderer ball, cup, flag, fence;
    readonly Mesh trailMesh;
    static MaterialPropertyBlock block;
    Vector3 axis, side, up;
    float turn, tilt, elapsed, duration;
    bool showcase;   // the title screen: just the world, turning slowly

    /// <summary>
    /// The replay's view of a point of S^3 (must match S3/Replay). Dropping y would put a point h up at cos(h),
    /// which is flat at the ground: a house would be as wide as ever but paper thin. 1 - h + Bend h^2 instead
    /// falls one for one with height at the ground, so things keep their true proportions there, and still
    /// reaches the centre at the pole, a quarter lap up.
    /// </summary>
    public static Vector3 ToBall(Vector4 p)
    {
        var g = new Vector3(p.x, p.w, p.z);
        float l = g.magnitude;
        if (l < 1e-9f) return Vector3.zero;
        float h = Mathf.Atan2(p.y, l);
        return g / l * (1 - h + Bend * h * h);
    }

    public S3Replay(S3World world, Camera mainCamera)
    {
        this.mainCamera = mainCamera;
        mainCamera.cullingMask &= ~(1 << Layer);

        var go = new GameObject("Replay Camera") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(world.transform, false);
        owned.Add(go);
        camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Space;
        camera.cullingMask = 1 << Layer;
        camera.fieldOfView = 42;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 20;
        camera.depth = mainCamera.depth + 1;
        camera.enabled = false;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.None;   // MSAA (the pipeline asset) handles edges; SMAA on top only blurs

        Color shellTint = world.biome == S3World.Biome.Basin ? new Color(0.78f, 0.86f, 0.95f)
                        : world.biome == S3World.Biome.Desert ? new Color(0.86f, 0.70f, 0.52f) : new Color(0.42f, 0.68f, 0.40f);
        // the far half of the shell, then the solid things inside it, then the scenery glass (over them, writing no
        // depth, so the flight shows through whatever stands in front of it), and the near half of the shell on top
        var shellBack = MakeMaterial(3000, shellTint, 0.55f, ShellAlpha, true, CullMode.Front);
        var glass = MakeMaterial(3001, new Color(0.78f, 0.78f, 0.82f), 0.5f, SceneryAlpha, false, CullMode.Off);
        var shellFront = MakeMaterial(3002, shellTint, 0.55f, ShellAlpha, true, CullMode.Back);
        var solid = MakeMaterial(2000, Color.white, 0.6f, 1, false, CullMode.Off);
        // the path and the ball draw after all the glass (still behind anything solid), so trees, houses and the
        // shell never dim them: the path reads clearly wherever it runs
        var ballMaterial = MakeMaterial(3003, Color.white, 1, 1, false, CullMode.Off, false, true);
        var cupMaterial = MakeMaterial(2000, new Color(0.95f, 0.2f, 0.15f), 1, 1, false, CullMode.Off, false);
        trailMaterial = MakeMaterial(3003, Color.white, 1, 1, false, CullMode.Off, false, true);

        Piece("Replay Shell Back", world.GroundMesh, shellBack);
        Piece("Replay Shell Front", world.GroundMesh, shellFront);
        Piece("Replay Scenery", world.SceneryMesh, glass);
        fence = Piece("Replay Fence", null, glass);
        flag = Piece("Replay Flag", Own(S3Scenery.FlagMesh()), solid);
        var marker = Own(MarkerMesh());
        ball = Piece("Replay Ball", marker, ballMaterial);
        cup = Piece("Replay Cup", marker, cupMaterial);
        trailMesh = Own(new Mesh { name = "S3 Replay Trail" });
        SetMarker(Piece("Replay Trail", trailMesh, trailMaterial), Vector3.zero, 1);   // already in ball coordinates
    }

    T Own<T>(T o) where T : Object { owned.Add(o); return o; }

    Material MakeMaterial(int queue, Color tint, float ambient, float alpha, bool shell, CullMode cull, bool onS3 = true, bool opaque = false)
    {
        var baseMaterial = Resources.Load<Material>("S3Replay");
        var m = Own(baseMaterial ? new Material(baseMaterial) : new Material(Shader.Find("S3/Replay")));
        bool transparent = queue >= 3000 && !opaque;
        m.renderQueue = queue;
        m.SetColor("_Tint", tint);
        m.SetFloat("_Ambient", ambient);
        m.SetFloat("_Alpha", alpha);
        m.SetFloat("_Shell", shell ? 1 : 0);
        m.SetFloat("_FromS3", onS3 ? 1 : 0);
        m.SetFloat("_Cull", (float)cull);
        m.SetFloat("_ZWrite", transparent ? 0 : 1);
        m.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
        m.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        return m;
    }

    Renderer Piece(string name, Mesh mesh, Material material)
    {
        var go = Own(new GameObject(name) { hideFlags = HideFlags.DontSave, layer = Layer });
        go.transform.SetParent(camera.transform.parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        S3World.SetFrame(r, Matrix4x4.identity);
        return r;
    }

    static void SetMarker(Renderer r, Vector3 offset, float scale)
    {
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector("_Marker", new Vector4(offset.x, offset.y, offset.z, scale));
        r.SetPropertyBlock(block);
    }

    /// <summary>A unit icosphere in ball coordinates (the ball and cup markers).</summary>
    static Mesh MarkerMesh()
    {
        var mb = new MeshBuilder(10f);
        mb.Icosphere(Vector3.zero, Vector3.one, 2, Color.white);
        return mb.ToMesh("S3 Replay Marker");
    }

    /// <summary>
    /// Start the replay of a hole's shots, in order: each one's samples of S^3 (one every <see cref="Sample"/>
    /// seconds of flight), then the cup, the flag's frame and the hole's fence (if any).
    /// </summary>
    public void Begin(IList<List<Vector4>> shots, Vector4 cupPoint, Matrix4x4 flagFrame, Mesh fenceMesh)
    {
        track.Clear();
        starts.Clear();
        ends.Clear();
        foreach (var shot in shots)
        {
            if (shot.Count < 2) continue;
            // a beat where the last one stopped before the next is hit
            if (track.Count > 0) for (int i = 0; i < Pause; i++) track.Add(track[track.Count - 1]);
            starts.Add(track.Count);
            track.AddRange(shot);
            ends.Add(track.Count - 1);
        }
        if (track.Count < 2) return;
        // framed on the hole: from the tee (where the first shot was hit) to the cup
        Vector3 from = ToBall(track[0]), c = ToBall(cupPoint).normalized;
        // the cup hangs straight down, so every replay is framed the same way whatever the hole
        up = -c;
        // and the camera looks along the normal of the shot's plane, which shows the arc face on rather than
        // edge on and is square to that "down" by construction; a shot straight to the antipode leaves the plane
        // undefined, and any direction level with the cup will do
        Vector3 normal = Vector3.Cross(from, c);
        axis = normal.magnitude > 0.05f ? normal.normalized
             : Vector3.Cross(c, Mathf.Abs(c.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        side = Vector3.Cross(up, axis);
        // it starts lifted a little above that level, looking slightly down on the world with the cup still low
        turn = 0;
        tilt = EyeTilt;
        elapsed = 0;
        duration = (track.Count - 1) * Sample;
        Age = 0;

        BuildTrail();
        S3World.SetFrame(flag, flagFrame);
        SetMarker(cup, ToBall(cupPoint), 0.022f);
        fence.GetComponent<MeshFilter>().sharedMesh = fenceMesh;
        fence.enabled = fenceMesh;
        showcase = false;
        ball.enabled = true;
        Shader.SetGlobalVector("_ReplayLight", Light);
        Active = true;
        camera.enabled = true;
        mainCamera.enabled = false;
        Update(0, Vector2.zero);
    }

    /// <summary>
    /// The title screen's backdrop: the whole world as the glass ball, with the first hole's flag in it, turning
    /// slowly. Nothing plays; there is no ball or path.
    /// </summary>
    public void BeginShowcase(Vector4 cupPoint, Matrix4x4 flagFrame)
    {
        track.Clear();
        starts.Clear();
        ends.Clear();
        track.Add(cupPoint);
        track.Add(cupPoint);
        Vector3 c = ToBall(cupPoint).normalized;
        up = -c;
        axis = Vector3.Cross(c, Mathf.Abs(c.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        side = Vector3.Cross(up, axis);
        turn = 0;
        tilt = 30;
        elapsed = 0;
        duration = Sample;
        Age = 0;
        showcase = true;
        BuildTrail();   // no shots: empty
        S3World.SetFrame(flag, flagFrame);
        SetMarker(cup, ToBall(cupPoint), 0.022f);
        fence.enabled = false;
        ball.enabled = false;
        Shader.SetGlobalVector("_ReplayLight", Light);
        Active = true;
        camera.enabled = true;
        mainCamera.enabled = false;
        Update(0, Vector2.zero);
    }

    public void End()
    {
        if (!Active) return;
        Active = false;
        showcase = false;
        if (ball) ball.enabled = true;
        if (camera) camera.enabled = false;
        if (mainCamera) mainCamera.enabled = true;
    }

    /// <summary>Advance (it runs again and again, a beat after the ball drops) and orbit with the mouse delta.</summary>
    public void Update(float dt, Vector2 mouse)
    {
        if (!Active) return;
        Age += dt;
        elapsed += dt * Speed;
        if (elapsed > duration + Tail * Speed) elapsed = 0;
        turn -= mouse.x * Look + (showcase ? 7f * dt : 0);   // the title screen turns by itself
        tilt = Mathf.Clamp(tilt + mouse.y * Look, -MaxTilt, MaxTilt);

        float t = turn * Mathf.Deg2Rad, l = tilt * Mathf.Deg2Rad;
        Vector3 level = axis * Mathf.Cos(t) + side * Mathf.Sin(t);
        Vector3 eye = (level * Mathf.Cos(l) + up * Mathf.Sin(l)) * EyeDistance;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-eye, up));   // always at the world's centre

        float position = Progress * (track.Count - 1);
        int first = Mathf.FloorToInt(position), second = Mathf.Min(first + 1, track.Count - 1);
        Vector4 here = Vector4.Lerp(track[first], track[second], position - first);
        SetMarker(ball, ToBall(here), 0.026f);
        trailMaterial.SetFloat("_Reveal", position);
    }

    /// <summary>
    /// Every shot's path as a tube in ball coordinates, each in its own colour (paler where it was hit), with a dot
    /// where it was hit. uv.x is the sample in the whole replay, so a path draws itself as its shot plays.
    /// </summary>
    void BuildTrail()
    {
        const int sides = 8;
        var pts = new Vector3[track.Count];
        for (int i = 0; i < track.Count; i++) pts[i] = ToBall(track[i]);
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var dot = MarkerMesh();
        var dotVerts = dot.vertices;
        var dotTris = dot.triangles;
        Object.Destroy(dot);
        for (int shot = 0; shot < starts.Count; shot++)
        {
            int from = starts[shot], to = ends[shot];
            Color col = ShotColors[shot % ShotColors.Length];
            Vector3 tangent = Vector3.forward, normal = Vector3.zero;
            int ring0 = verts.Count;
            for (int i = from; i <= to; i++)
            {
                Vector3 d = pts[Mathf.Min(i + 1, to)] - pts[Mathf.Max(i - 1, from)];
                if (d.sqrMagnitude > 1e-14f) tangent = d.normalized;   // a ball at rest keeps the last heading
                // carry the ring's orientation along the path (parallel transport), so the tube doesn't twist
                normal -= Vector3.Dot(normal, tangent) * tangent;
                if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right);
                normal.Normalize();
                Vector3 binormal = Vector3.Cross(tangent, normal);
                var c = Color.Lerp(Color.white, col, 0.45f + 0.55f * (i - from) / Mathf.Max(1f, to - from));
                for (int k = 0; k < sides; k++)
                {
                    float a = 2 * Mathf.PI * k / sides;
                    verts.Add(pts[i] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * TrailRadius);
                    colors.Add(c);
                    uvs.Add(new Vector2(i, 0));
                }
            }
            for (int i = 0; i < to - from; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = ring0 + i * sides + k, b = ring0 + i * sides + (k + 1) % sides;
                    tris.Add(a); tris.Add(a + sides); tris.Add(b);
                    tris.Add(b); tris.Add(a + sides); tris.Add(b + sides);
                }
            // the dot where this shot was hit, appearing as it's hit
            int dot0 = verts.Count;
            foreach (var v in dotVerts) { verts.Add(pts[from] + v * StartDot); colors.Add(col); uvs.Add(new Vector2(from, 0)); }
            foreach (int t in dotTris) tris.Add(dot0 + t);
        }
        trailMesh.Clear();
        trailMesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        trailMesh.SetVertices(verts);
        trailMesh.SetColors(colors);
        trailMesh.SetUVs(0, uvs);
        trailMesh.SetTriangles(tris, 0, false);
        trailMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // never cull
    }

    /// <summary>Removes the replay's camera, pieces, meshes and materials.</summary>
    public void Dispose()
    {
        End();
        foreach (var o in owned)
            if (o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
        owned.Clear();
    }
}
