using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Owns the 3-sphere world: builds the ground and scenery meshes, defines the golf course,
/// and pushes the camera / lighting uniforms that the "S3/Object" shader reads.
///
/// Runs in edit mode too. Every camera gets its own S^3 view just before it renders: the game
/// camera sees through the player's eyes, and the Scene view camera's position and rotation are
/// carried onto S^3 by the exponential map, so flying around the Scene view moves through the world.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class S3World : MonoBehaviour
{
    public static S3World Instance { get; private set; }

    [Serializable]
    public struct HoleDef
    {
        public Vector2 tee;   // local ground coordinates (x, z) in metres around the origin e_w
        public Vector2 cup;
        public int par;
        public HoleDef(float tx, float tz, float cx, float cz, int par) { tee = new Vector2(tx, tz); cup = new Vector2(cx, cz); this.par = par; }
    }

    public class Hole { public Vector4 tee, cup; public int par; }

    public struct Obstacle
    {
        public Vector4 foot;   // ground point
        public float radius;   // metres
        public float height;   // metres
        public Obstacle(Vector4 foot, float radius, float height) { this.foot = foot; this.radius = radius; this.height = height; }
    }

    /// <summary>The campaign: Meadow teaches golf, Desert teaches the laser, the Eternal Poles need both.</summary>
    public enum Biome { Meadow, Desert, Basin }

    /// <summary>A laser puzzle laid out around a ground frame (local x, z metres; yaw degrees from +z towards +x).</summary>
    [Serializable]
    public struct LockDef
    {
        public S3Levels.Template template;
        public float x, z, yaw;
        public bool mirror;
        public LockDef(S3Levels.Template template, float x, float z, float yaw, bool mirror = false)
        { this.template = template; this.x = x; this.z = z; this.yaw = yaw; this.mirror = mirror; }
    }

    /// <summary>Per-map lighting and air. Colours are authored in sRGB like the vertex colours.</summary>
    [Serializable]
    public class Style
    {
        public Vector4 sun;            // fixed R^4 vector; its tangent part at a point is the light direction
        public Color sunColor;
        public Color skyAmbient;       // light from "up" (away from the ground)
        public Color groundAmbient;    // bounce light from below
        public Color haze;             // the air near the ground (what the horizon fades to)
        public Color skyColor;         // the air looking up, and what glass, ice and water reflect
        [Tooltip("How thick the air is at the ground: fraction of light lost per metre.")]
        public float fogDensity;
        [Tooltip("The air thins out by e every this many metres up, so looking up (at the antipode) stays clear.")]
        public float fogHeight;
        [Tooltip("How much the air glows looking towards the sun.")]
        public float sunGlow;
        [Tooltip("How far the air shifts from haze to sky colour looking up.")]
        public float skyTint;
        public Color shadowTint;       // baked contact shadows on the ground
        public float shadowStrength;
    }

    [Header("Map")]
    public Biome biome = Biome.Meadow;

    [Header("Geometry")]
    [Tooltip("Radius of the 3-sphere in metres. Walking 2*pi*R in a straight line brings you back.")]
    public float radius = 12f;
    public int seed = 11;

    [Header("Rendering")]
    public Material objectMaterial;
    public Material groundMaterial;
    [Tooltip("S3/Beam: laser light.")]
    public Material beamMaterial;
    [Tooltip("S3/Glow: the barrier over a sealed cup.")]
    public Material glowMaterial;
    [Tooltip("S3/Particles: pollen, dust and snow drifting around the eye.")]
    public Material particleMaterial;
    [Tooltip("Global post-processing volume; its profile is swapped per map.")]
    public Volume postVolume;
    public VolumeProfile meadowPost;
    public VolumeProfile desertPost;
    public VolumeProfile basinPost;
    public Style meadowStyle = MeadowLook;
    public Style desertStyle = DesertLook;
    public Style basinStyle = BasinLook;

    // The maps' looks as designed (S3 > Reset Map Styles copies them into the scene).
    public static Style MeadowLook => new Style
    {
        sun = new Vector4(0.55f, 0.75f, 0.35f, 0.2f),
        sunColor = new Color(0.80f, 0.74f, 0.60f),
        skyAmbient = new Color(0.60f, 0.62f, 0.66f),
        groundAmbient = new Color(0.50f, 0.48f, 0.36f),
        haze = new Color(0.78f, 0.88f, 0.96f),
        skyColor = new Color(0.56f, 0.74f, 0.98f),
        fogDensity = 0.022f, fogHeight = 4f, sunGlow = 0.35f, skyTint = 0.85f,
        shadowTint = new Color(0.55f, 0.62f, 0.60f),
        shadowStrength = 0.55f,
    };
    public static Style DesertLook => new Style
    {
        sun = new Vector4(0.7f, 0.45f, 0.25f, 0.45f),   // low, golden-hour sun
        sunColor = new Color(1.00f, 0.76f, 0.50f),
        skyAmbient = new Color(0.56f, 0.48f, 0.54f),
        groundAmbient = new Color(0.60f, 0.44f, 0.32f),
        haze = new Color(0.98f, 0.80f, 0.66f),
        skyColor = new Color(0.96f, 0.70f, 0.56f),
        fogDensity = 0.035f, fogHeight = 5f, sunGlow = 0.35f, skyTint = 0.7f,
        shadowTint = new Color(0.62f, 0.52f, 0.62f),
        shadowStrength = 0.6f,
    };
    public static Style BasinLook => new Style
    {
        sun = new Vector4(0.8f, 0.35f, 0.3f, 0.4f),     // low, pale polar day
        sunColor = new Color(0.86f, 0.78f, 0.64f),
        skyAmbient = new Color(0.50f, 0.60f, 0.80f),
        groundAmbient = new Color(0.56f, 0.63f, 0.76f),
        haze = new Color(0.80f, 0.88f, 0.98f),
        skyColor = new Color(0.50f, 0.66f, 0.95f),
        fogDensity = 0.012f, fogHeight = 3f, sunGlow = 0.3f, skyTint = 0.85f,
        shadowTint = new Color(0.55f, 0.65f, 0.85f),
        shadowStrength = 0.5f,
    };
    [Tooltip("Depth range parameter: geometry nearer than about nearClip*R (or that close to the antipode) is clipped.")]
    public float nearClip = 0.004f;

    // Courses: holes of 28-33 m (a quarter lap is 18.8 m, so every cup is past the horizon) with a
    // building on the direct line, so the short way is blocked and going round the world or over
    // the pole competes with it. Hole 3 puts the cup at the tee's antipode (37.7 m every way).
    // Each tee starts a few metres from the previous cup.
    [Header("Course")]
    public HoleDef[] course =
    {
        new HoleDef(0, 0, 8, 32, 4),             // through the red-roofed house
        new HoleDef(7.3f, 29.1f, -8.4f, -14.9f, 4),
        new HoleDef(-5.4f, -14f, 8.2f, 21.2f, 5),   // antipode: every direction is 37.7 m
        new HoleDef(3.3f, 24f, 1.1f, -9.4f, 4),
        new HoleDef(2.7f, -6.7f, -19f, 12.1f, 4),
    };
    public float cupRadius = 0.62f;
    public float greenRadius = 3.5f;

    // The Desert has no golf: its five laser puzzles all stand around one spot, one at a time.
    [Header("Desert puzzles")]
    [Tooltip("Where the laser puzzles stand: local x, z metres, and yaw (degrees) of their north.")]
    public Vector3 puzzleFrame = new Vector3(0, 0, 0);

    // The Eternal Poles: every cup is sealed under an ice barrier until a laser lights the crystal
    // beside it. The locks are puzzles of their own (frozen mirrors, a prism, twin lasers, the
    // antipode), placed (and checked by S3Setup) so the solved light misses the tees and greens.
    // You start each hole on foot. The frozen lake is only scenery.
    [Header("Eternal Poles")]
    public HoleDef[] basinCourse =
    {
        // laid out by walking real distances on the sphere (14, 21, 25, 37.7 and 30 m), with every green
        // at least 11 m from the others and 8 m from the other holes' tees
        new HoleDef(0, -4, 6, 9, 3),                        // Snowfield: a gentle opener
        new HoleDef(4.56f, 5.69f, -13.13f, -5.96f, 4),
        new HoleDef(-9.49f, -7.66f, 20.92f, -13.16f, 4),
        new HoleDef(16.44f, -20.63f, -7.05f, 8.85f, 5),     // Polar Opposite: the cup is the tee's antipode
        new HoleDef(-4.91f, 5.96f, -11.18f, -27.35f, 5),    // The Long Night
    };
    public LockDef[] basinLocks =
    {
        // found by S3Setup.SearchLocks, checked by TestPuzzles and TestShortcuts
        new LockDef(S3Levels.Template.FrozenMirror, 5f, 2f, 240),
        new LockDef(S3Levels.Template.Prism, -3.13f, 4.04f, 240, true),
        new LockDef(S3Levels.Template.TwinBeams, 4.92f, -13.16f, 195, true),
        new LockDef(S3Levels.Template.Homecoming, -2.05f, 8.85f, 150),        // its mirror goes by the tee: the crystal's antipode
        new LockDef(S3Levels.Template.FrozenRelay, -16.18f, -12.35f, 270, true),
    };
    [Tooltip("The frozen lake: local x, z metres and radius.")]
    public Vector3 basinLake = new Vector3(0, 17.5f, 6.8f);
    [Tooltip("The ice barrier over a sealed cup (metres).")]
    public float barrierRadius = 3.1f;

    [NonSerialized] public readonly List<Hole> holes = new List<Hole>();
    [NonSerialized] public readonly List<Obstacle> obstacles = new List<Obstacle>();
    /// <summary>Desert: the puzzles in order. Basin: the lock on each hole's cup.</summary>
    [NonSerialized] public readonly List<S3Levels.Level> levels = new List<S3Levels.Level>();

    public bool HasGolf => biome != Biome.Desert;
    public bool HasLasers => biome != Biome.Meadow;
    public string MapName => biome == Biome.Desert ? "Desert Oasis" : biome == Biome.Basin ? "Eternal Poles" : "Meadow";

    public Style ActiveStyle => biome == Biome.Desert ? desertStyle : biome == Biome.Basin ? basinStyle : meadowStyle;

    // ---- props that dither away when they block the view (faded on the CPU, one value per prop)
    public const int MaxFadeProps = 512;
    struct FadeProp { public Vector4 foot; public float radius, height; }
    readonly List<FadeProp> fadeProps = new List<FadeProp>();
    readonly float[] fades = new float[MaxFadeProps];

    // ---- baked contact shadows and desert grass patches
    struct Caster { public Vector4 foot; public float radius, strength; }
    readonly List<Caster> casters = new List<Caster>();
    public const int MaxPatches = 16;
    readonly Vector4[] patches = new Vector4[MaxPatches];
    readonly float[] patchRadii = new float[MaxPatches];
    int patchCount;

    static readonly int ModelID = Shader.PropertyToID("_S3Model");
    static readonly int ViewID = Shader.PropertyToID("_S3View");
    static readonly int EyeID = Shader.PropertyToID("_S3Eye");
    static readonly int FadeID = Shader.PropertyToID("_S3PropFade");
    static readonly int PatchID = Shader.PropertyToID("_S3Patch");
    static readonly int PatchRID = Shader.PropertyToID("_S3PatchR");
    static MaterialPropertyBlock mpb;

    static CommandBuffer cameraCmd;
    S3Player player;

    void Awake()
    {
        if (Application.isPlaying) Build();
    }

    void OnEnable()
    {
        Instance = this;
        if (built) PushGlobals();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        if (!Application.isPlaying) Build();   // edit-mode preview (children are DontSave, never written to the scene)
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (!Application.isPlaying) ClearChildren();
    }

    bool built;

#if UNITY_EDITOR
    /// <summary>Rebuild the edit-mode preview whenever a value changes in the Inspector.</summary>
    void OnValidate()
    {
        if (Application.isPlaying || !built) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled && !Application.isPlaying) Build();
        };
    }
#endif

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i).gameObject;
            var mesh = c.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;   // generated, not an asset
            if (Application.isPlaying) { Destroy(c); if (mesh) Destroy(mesh); }
            else { DestroyImmediate(c); if (mesh) DestroyImmediate(mesh); }
        }
    }

    /// <summary>Sets the S^3 view for whichever camera is about to render.</summary>
    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        Matrix4x4 frame;
        if (cam.cameraType == CameraType.SceneView) frame = SceneViewFrame(cam.transform);
        else
        {
            if (!player) player = FindAnyObjectByType<S3Player>();
            if (!player) return;
            frame = player.ViewFrame();
        }
        if (cam.cameraType != CameraType.SceneView) UpdateParticles(frame);
        Matrix4x4 view = ViewMatrix(frame);
        cameraCmd ??= new CommandBuffer { name = "S3 camera" };
        cameraCmd.Clear();
        cameraCmd.SetGlobalMatrix(ViewID, view);
        cameraCmd.SetGlobalVector(EyeID, frame.GetColumn(3));
        context.ExecuteCommandBuffer(cameraCmd);
        cameraCmd.Clear();
        Shader.SetGlobalMatrix(ViewID, view);
        Shader.SetGlobalVector(EyeID, frame.GetColumn(3));
    }

    /// <summary>
    /// A Euclidean camera pose read as a point in the exp-map chart around e_w: walk |p| metres
    /// along the geodesic towards p, carrying the camera's axes with it.
    /// </summary>
    public Matrix4x4 SceneViewFrame(Transform t)
    {
        Vector3 p = t.position;
        float d = p.magnitude;
        Matrix4x4 walk = d > 1e-6f ? S3.PlaneRotation(S3.EW, new Vector4(p.x / d, p.y / d, p.z / d, 0), d / radius) : Matrix4x4.identity;
        return walk * Matrix4x4.Rotate(t.rotation);
    }

    static Matrix4x4 ViewMatrix(Matrix4x4 cameraFrame) => Matrix4x4.Scale(new Vector3(1, 1, -1)) * cameraFrame.transpose;

    // ------------------------------------------------------------------ ambient particles

    Renderer particles;
    Vector4 particleBox;
    bool particleInit;
    Vector4 particleFoot, particleFwd;
    Vector2 particleShift;
    static MaterialPropertyBlock particleBlock;

    /// <summary>
    /// Keeps the box of particles on the eye's foot. Its heading is carried along (parallel transport)
    /// rather than turning with the view, and how far the eye walks is passed to the shader, so the
    /// particles hold still in the world as the box follows.
    /// </summary>
    void UpdateParticles(Matrix4x4 eye)
    {
        if (!particles) return;
        Vector4 foot = S3.Foot(eye.GetColumn(3));
        Vector4 fwd = particleInit ? S3.Ortho(particleFwd, foot, S3.EY) : Vector4.zero;
        if (fwd == Vector4.zero) fwd = S3.Ortho(S3.EZ, foot, S3.EY);
        if (fwd == Vector4.zero) fwd = S3.Ortho(S3.EX, foot, S3.EY);
        Matrix4x4 frame = S3.FrameAt(foot, S3.EY, fwd);
        if (particleInit)
        {
            Vector4 step = (foot - particleFoot) * radius;
            if (step.sqrMagnitude < 4f)   // a jump (new map, new hole) just starts afresh
            {
                particleShift.x = Mathf.Repeat(particleShift.x + Vector4.Dot(step, frame.GetColumn(0)), particleBox.x);
                particleShift.y = Mathf.Repeat(particleShift.y + Vector4.Dot(step, frame.GetColumn(2)), particleBox.z);
            }
        }
        particleFoot = foot;
        particleFwd = frame.GetColumn(2);
        particleInit = true;
        SetFrame(particles, frame);
        particleBlock ??= new MaterialPropertyBlock();
        particles.GetPropertyBlock(particleBlock);
        particleBlock.SetVector("_S3PShift", new Vector4(particleShift.x, 0, particleShift.y, 0));
        particleBlock.SetVector("_S3PBox", particleBox);
        particles.SetPropertyBlock(particleBlock);
    }

    /// <summary>(Re)generates the ground, scenery and course. Safe to call in edit mode.</summary>
    public void Build()
    {
        Instance = this;
        ClearChildren();
        holes.Clear();
        obstacles.Clear();
        fadeProps.Clear();
        casters.Clear();
        patchCount = 0;
        Array.Clear(fades, 0, fades.Length);
        if (biome != Biome.Desert)
            foreach (var h in biome == Biome.Basin ? basinCourse : course)
                holes.Add(new Hole { tee = S3.GroundPoint(h.tee.x, h.tee.y, radius), cup = S3.GroundPoint(h.cup.x, h.cup.y, radius), par = h.par });
        BuildLevels();

        var scenery = S3Scenery.Build(this, new System.Random(seed));   // registers obstacles, fades, shadows, patches
        CreateRenderer("Scenery", scenery.ToMesh("S3 Scenery"), objectMaterial, Matrix4x4.identity);

        if (particleMaterial)
        {
            var mesh = S3Scenery.AmbientParticles(biome, radius, out particleBox);
            particles = CreateRenderer("Ambient Particles", mesh, particleMaterial, Matrix4x4.identity).GetComponent<Renderer>();
            particleInit = false;
        }

        var ground = new MeshBuilder();
        ground.GroundDisk(radius, 160, 320, Color.white);   // fine enough for crisp baked shadows
        BakeGroundShadows(ground);
        CreateRenderer("Ground", ground.ToMesh("S3 Ground"), groundMaterial, Matrix4x4.identity);

        if (postVolume)
        {
            var profile = biome == Biome.Desert ? desertPost : biome == Biome.Basin ? basinPost : meadowPost;
            if (profile && postVolume.sharedProfile != profile) postVolume.sharedProfile = profile;
        }
        PushGlobals();
        Shader.SetGlobalFloatArray(FadeID, fades);
        built = true;
    }

    /// <summary>Switch maps (rebuilds everything; the golf game restarts itself).</summary>
    public void SetBiome(Biome b)
    {
        biome = b;
        Build();
    }

    // ------------------------------------------------------------------ registration (called by S3Scenery)

    /// <summary>Registers a prop that should dither away when it blocks the view; returns its shader index (1-based).</summary>
    public int RegisterFadeProp(Vector4 foot, float radius, float height)
    {
        if (fadeProps.Count >= MaxFadeProps) return 0;
        fadeProps.Add(new FadeProp { foot = foot, radius = radius, height = height });
        return fadeProps.Count;
    }

    /// <summary>A soft contact shadow on the ground, pushed away from the sun by the caster's height.</summary>
    public void RegisterShadow(Vector4 foot, float radius, float height, float strength = 1f)
    {
        Vector4 L = S3.Tangent(foot, ActiveStyle.sun.normalized);
        float up = Mathf.Max(Vector4.Dot(L, S3.EY), 0.25f);
        Vector4 horizontal = L - Vector4.Dot(L, S3.EY) * S3.EY;
        float h = horizontal.magnitude;
        if (h > 1e-5f) foot = S3.Walk(foot, -horizontal / h, Mathf.Min(0.45f * height * h / up, 3f) / this.radius);
        casters.Add(new Caster { foot = foot, radius = radius, strength = strength });
    }

    public void RegisterPatch(Vector4 foot, float radiusM)
    {
        if (patchCount >= MaxPatches) return;
        patches[patchCount] = foot;
        patchRadii[patchCount] = radiusM;
        patchCount++;
    }

    void BakeGroundShadows(MeshBuilder ground)
    {
        Style st = ActiveStyle;
        Color tint = st.shadowTint;
        // flat arrays and squared chords: this runs for every ground vertex against every caster
        int n = casters.Count;
        var cx = new float[n]; var cy = new float[n]; var cz = new float[n]; var cw = new float[n];
        var reach2 = new float[n]; var falloff = new float[n]; var amount = new float[n];
        for (int c = 0; c < n; c++)
        {
            var k = casters[c];
            cx[c] = k.foot.x; cy[c] = k.foot.y; cz[c] = k.foot.z; cw[c] = k.foot.w;
            float reach = 2.5f * k.radius / radius;                // in unit-sphere chord length
            reach2[c] = reach * reach;
            falloff[c] = radius * radius / (k.radius * k.radius);  // chord^2 (unit) -> (metres / radius)^2
            amount[c] = k.strength * st.shadowStrength;
        }
        for (int i = 0; i < ground.verts.Count; i++)
        {
            Vector4 p = S3.Exp(ground.verts[i], radius);
            float lit = 1;
            for (int c = 0; c < n; c++)
            {
                float dx = p.x - cx[c], dy = p.y - cy[c], dz = p.z - cz[c], dw = p.w - cw[c];
                float d2 = dx * dx + dy * dy + dz * dz + dw * dw;   // chord^2 ~ (metres / R)^2 for nearby points
                if (d2 > reach2[c]) continue;
                lit *= 1 - amount[c] * Mathf.Exp(-d2 * falloff[c]);
            }
            ground.colors[i] = Color.Lerp(tint, Color.white, lit);
        }
    }

    // ------------------------------------------------------------------ see-through props

    /// <summary>
    /// Per prop: 1 if the eye is inside its bounding cylinder or the sight line to the ball passes
    /// through it, eased over time so props fade rather than pop. Uploaded as one float per prop.
    /// </summary>
    public void UpdateFades(Vector4 eye, Vector4 ball, float dt)
    {
        float L = S3.Dist(eye, ball);
        Vector4 d = S3.DirTo(eye, ball);
        Vector4 eyeFoot = S3.Foot(eye);
        float step = dt * 5f;
        for (int i = 0; i < fadeProps.Count; i++)
        {
            var fp = fadeProps[i];
            float target = 0;
            // broad phase: farther from the eye than the whole sight line plus the prop
            if (S3.Dist(eyeFoot, fp.foot) * radius < L * radius + fp.radius + 2f)
                for (int k = 0; k < 9; k++)
                {
                    float t = L * (k / 9f);                         // eye ... just short of the ball
                    Vector4 S = Mathf.Cos(t) * eye + Mathf.Sin(t) * d;
                    float h = Mathf.Asin(Mathf.Clamp(S.y, -1f, 1f)) * radius;
                    var sf = new Vector4(S.x, 0, S.z, S.w);
                    float sl = sf.magnitude;
                    if (sl < 1e-5f) continue;
                    float hd = S3.Dist(sf / sl, fp.foot) * radius;   // horizontal distance to the prop axis
                    float margin = k == 0 ? 1f : 0.4f;              // a little extra room around the eye
                    float inside = (1 - Mathf.Clamp01((hd - fp.radius) / margin)) *
                                   (1 - Mathf.Clamp01((h - fp.height) / 0.5f));
                    target = Mathf.Max(target, inside);
                }
            fades[i] = Mathf.MoveTowards(fades[i], target, step);
        }
        Shader.SetGlobalFloatArray(FadeID, fades);
    }

    public void ClearFades()
    {
        Array.Clear(fades, 0, fades.Length);
        Shader.SetGlobalFloatArray(FadeID, fades);
    }

    public GameObject CreateRenderer(string name, Mesh mesh, Material material, Matrix4x4 frame)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        SetFrame(r, frame);
        return go;
    }

    /// <summary>Places a renderer's local-metre mesh at an SO(4) frame.</summary>
    public static void SetFrame(Renderer r, Matrix4x4 frame)
    {
        mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetMatrix(ModelID, frame);
        r.SetPropertyBlock(mpb);
    }

    /// <summary>Per-renderer glow, see-through dither (holograms) and tint for the S3/Object shader.</summary>
    public static void SetLook(Renderer r, float glow, float dither, Color tint)
    {
        mpb ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetFloat("_Glow", glow);
        mpb.SetFloat("_Dither", dither);
        mpb.SetColor("_Color", tint);
        r.SetPropertyBlock(mpb);
    }

    /// <summary>A ground frame from local (x, z) metres and a yaw in degrees.</summary>
    public Matrix4x4 LocalFrame(float x, float z, float yawDeg) => S3.GroundFrame(x, z, yawDeg * Mathf.Deg2Rad, radius);

    /// <summary>Lays out the Desert puzzles, or the lock on every Basin cup.</summary>
    void BuildLevels()
    {
        levels.Clear();
        if (biome == Biome.Desert)
        {
            var frame = LocalFrame(puzzleFrame.x, puzzleFrame.y, puzzleFrame.z);
            foreach (var t in S3Levels.DesertOrder) levels.Add(S3Levels.Build(t, frame, radius));
        }
        else if (biome == Biome.Basin)
            for (int i = 0; i < holes.Count && i < basinLocks.Length; i++)
                levels.Add(BuildLock(basinLocks[i]));
    }

    public S3Levels.Level BuildLock(LockDef d) => S3Levels.Build(d.template, LocalFrame(d.x, d.z, d.yaw), radius, d.mirror);

    void PushGlobals()
    {
        Style st = ActiveStyle;
        Shader.SetGlobalFloat("_S3R", radius);
        Shader.SetGlobalFloat("_S3InvR", 1f / radius);
        Shader.SetGlobalFloat("_S3Near", nearClip);
        Shader.SetGlobalVector("_S3Sun", st.sun.normalized);
        Shader.SetGlobalVector("_S3SunColor", st.sunColor);
        Shader.SetGlobalVector("_S3SkyAmbient", st.skyAmbient);
        Shader.SetGlobalVector("_S3GroundAmbient", st.groundAmbient);
        Shader.SetGlobalVector("_S3Haze", st.haze);
        Shader.SetGlobalVector("_S3SkyColor", st.skyColor);
        Shader.SetGlobalVector("_S3Fog", new Vector4(st.fogDensity, st.fogHeight, st.sunGlow, st.skyTint));
        Shader.SetGlobalFloat("_S3Biome", (float)biome);
        Shader.SetGlobalFloat("_S3CupR", cupRadius);
        Shader.SetGlobalFloat("_S3GreenR", HasGolf ? greenRadius : 0);   // no greens without golf
        Shader.SetGlobalVector("_S3BallInfo", Vector4.zero);              // S3Golf turns the ball's shadow on
        Shader.SetGlobalVectorArray(PatchID, patches);
        Shader.SetGlobalFloatArray(PatchRID, patchRadii);
        Shader.SetGlobalFloat("_S3PatchCount", patchCount);
    }

    /// <summary>Camera frame in SO(4): columns (right, up, forward, eye position).</summary>
    public void SetCamera(Matrix4x4 cameraFrame)
    {
        Shader.SetGlobalMatrix(ViewID, ViewMatrix(cameraFrame));
        Shader.SetGlobalVector(EyeID, cameraFrame.GetColumn(3));
    }

    /// <summary>The hole being played: its green and cup, and the mown tee.</summary>
    public void SetActiveHole(Vector4 tee, Vector4 cup)
    {
        Shader.SetGlobalVector("_S3Hole", cup);
        Shader.SetGlobalVector("_S3Tee", tee);
    }

    /// <summary>The golf ball, for its shadow on the ground (radius in metres; 0 hides it).</summary>
    public static void SetBall(Vector4 position, float radius)
    {
        Shader.SetGlobalVector("_S3Ball", position);
        Shader.SetGlobalVector("_S3BallInfo", new Vector4(radius, radius > 0 ? 1 : 0, 0, 0));
    }

    /// <summary>Ground distance in metres between two points (their feet).</summary>
    public float GroundDistance(Vector4 a, Vector4 b) => S3.Dist(S3.Foot(a), S3.Foot(b)) * radius;
}
